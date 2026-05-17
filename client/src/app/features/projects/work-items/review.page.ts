import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WorkItemsService } from '../../../core/api/work-items.service';
import type {
  CheckpointContractView,
  CheckpointSignalDto,
  WorkItemDto,
} from '../../../core/api/work-items.types';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type { ProjectDto, ProjectMembershipDto } from '../../../core/api/workspace.types';
import { AuthService } from '../../../core/auth/auth.service';
import type { AppError } from '../../../core/errors/app-error';
import { ActiveStepArtefactPanel } from './components/active-step-artefact-panel';
import { AssignmentConfirmPanel, type AssignmentConfirmSubmit } from './components/assignment-confirm-panel';
import { CheckpointActionBar, type SubmittedOutcome } from './components/checkpoint-action-bar';
import { DecisionHistoryList } from './components/decision-history-list';
import { LifecycleTimeline, type TimelineStep } from './components/lifecycle-timeline';
import { StreamFeed } from './components/stream-feed';

type ErrorKind = 'forbidden' | 'not-found' | 'other';

interface ExecutorStep { key: string; label?: string; artefact?: unknown; }

const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled']);

@Component({
  selector: 'review-page',
  standalone: true,
  imports: [
    RouterLink,
    LifecycleTimeline,
    ActiveStepArtefactPanel,
    DecisionHistoryList,
    CheckpointActionBar,
    AssignmentConfirmPanel,
    StreamFeed,
  ],
  templateUrl: './review.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewPage {
  private readonly ws = inject(WorkspaceService);
  private readonly workItems = inject(WorkItemsService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly loading = signal(true);
  protected readonly project = signal<ProjectDto | null>(null);
  protected readonly workItem = signal<WorkItemDto | null>(null);
  protected readonly signals = signal<CheckpointSignalDto[]>([]);
  protected readonly contract = signal<CheckpointContractView | null>(null);
  protected readonly selectedStepKey = signal<string | null>(null);

  protected readonly errorKind = signal<ErrorKind | null>(null);
  protected readonly error = signal<AppError | null>(null);

  protected readonly submitting = signal<string | null>(null);
  protected readonly submitError = signal<AppError | null>(null);

  // FEAT-009 / T-070: memberships fed to AssignmentConfirmPanel when the active contract
  // is per-task + assignment-confirmed. Fetched lazily on contract resolution.
  protected readonly memberships = signal<ProjectMembershipDto[]>([]);

  protected readonly isAssignmentConfirmCheckpoint = computed(() => {
    const c = this.contract();
    return c !== null && c.perTask === true && c.checkpointKey === 'assignment-confirmed';
  });

  // Build the timeline by merging executorState.steps with signals.
  protected readonly timeline = computed<TimelineStep[]>(() => {
    const wi = this.workItem();
    if (!wi) return [];
    const steps = readExecutorSteps(wi.executorState);
    if (steps.length === 0) return [];
    const signalsByKey = new Map<string, CheckpointSignalDto>();
    for (const s of this.signals()) {
      signalsByKey.set(s.checkpointKey, s);
    }
    return steps.map(step => {
      const signal = signalsByKey.get(step.key);
      const isActive = wi.currentCheckpointKey === step.key && wi.currentStatus === 'WaitingOnCheckpoint';
      let state: TimelineStep['state'] = 'pending';
      if (signal) state = signal.outcome === 'reject' ? 'rejected' : 'approved';
      if (isActive) state = 'active';
      return {
        key: step.key,
        label: step.label ?? step.key,
        state,
        signal: signal
          ? { signaledBy: signal.signaledBy.displayName, signaledAt: signal.signaledAt, outcome: signal.outcome }
          : null,
      };
    });
  });

  protected readonly activeStepKey = computed(() => {
    const wi = this.workItem();
    return wi?.currentCheckpointKey ?? null;
  });

  // The step currently displayed in the artefact panel — defaults to the active step, but the user
  // can click a past step in the timeline to inspect its artefact.
  protected readonly displayedStepKey = computed(() => this.selectedStepKey() ?? this.activeStepKey());

  protected readonly displayedArtefact = computed(() => {
    const wi = this.workItem();
    const key = this.displayedStepKey();
    if (!wi || !key) return null;
    const steps = readExecutorSteps(wi.executorState);
    return steps.find(s => s.key === key)?.artefact ?? null;
  });

  protected readonly hasActiveCheckpoint = computed(() => {
    const wi = this.workItem();
    return wi !== null && !TERMINAL_STATUSES.has(wi.currentStatus) && wi.currentCheckpointKey !== null;
  });

  protected readonly callerHasRole = computed(() => {
    const c = this.contract();
    if (!c) return false;
    if (this.auth.isOperator()) return true;
    const p = this.project();
    if (!p) return false;
    const m = this.auth.memberships().find(x => x.projectId === p.id);
    return m ? m.roles.includes(c.requiredRoleKey) : false;
  });

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(async params => {
      const slug = params.get('slug');
      const id = params.get('id');
      if (!slug || !id) return;
      await this.load(slug, id);
    });
  }

  private async load(slug: string, workItemId: string): Promise<void> {
    this.loading.set(true);
    this.errorKind.set(null);
    this.error.set(null);
    this.workItem.set(null);
    this.signals.set([]);
    this.contract.set(null);
    this.selectedStepKey.set(null);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.project.set(project);
      const [wi, signalsEnv] = await Promise.all([
        this.workItems.get(project.id, workItemId),
        this.workItems.listSignals(project.id, workItemId, { page: 1, pageSize: 50 }),
      ]);
      this.workItem.set(wi);
      this.signals.set(signalsEnv.data);
      if (wi.currentCheckpointKey && wi.currentStatus === 'WaitingOnCheckpoint') {
        try {
          const contract = await this.workItems.getCheckpoint(project.id, workItemId, wi.currentCheckpointKey);
          this.contract.set(contract);
          // Only fetch memberships when the per-task assignment panel will render — skip on
          // every other checkpoint to keep the page snappy.
          if (contract.perTask && contract.checkpointKey === 'assignment-confirmed') {
            await this.loadMemberships(project.id);
          }
        } catch {
          // Contract may not exist (404 if executor didn't declare it). The bar shows read-only.
        }
      }
    } catch (e: unknown) {
      this.classify(e);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadMemberships(projectId: string): Promise<void> {
    try {
      const rows = await this.ws.listMemberships(projectId);
      this.memberships.set(rows);
    } catch {
      // Non-fatal — the panel falls back to free-text-only when the list is empty.
      this.memberships.set([]);
    }
  }

  protected onStepSelected(step: TimelineStep): void {
    this.selectedStepKey.set(step.key);
  }

  protected onCheckpointResolved(): void {
    // Debounced refetch: just queue once; the page is cheap to reload.
    const p = this.project();
    const wi = this.workItem();
    if (!p || !wi) return;
    window.setTimeout(() => { void this.refetchOnly(p.id, wi.id); }, 250);
  }

  private async refetchOnly(projectId: string, workItemId: string): Promise<void> {
    try {
      const [wi, signalsEnv] = await Promise.all([
        this.workItems.get(projectId, workItemId),
        this.workItems.listSignals(projectId, workItemId, { page: 1, pageSize: 50 }),
      ]);
      this.workItem.set(wi);
      this.signals.set(signalsEnv.data);
      if (wi.currentCheckpointKey && wi.currentStatus === 'WaitingOnCheckpoint') {
        const contract = await this.workItems.getCheckpoint(projectId, workItemId, wi.currentCheckpointKey);
        this.contract.set(contract);
        if (contract.perTask && contract.checkpointKey === 'assignment-confirmed' && this.memberships().length === 0) {
          await this.loadMemberships(projectId);
        }
      } else {
        this.contract.set(null);
      }
    } catch { /* best-effort */ }
  }

  protected async onActionSubmitted(action: SubmittedOutcome): Promise<void> {
    const p = this.project();
    const wi = this.workItem();
    if (!p || !wi || !wi.currentCheckpointKey) return;
    this.submitting.set(action.outcome);
    this.submitError.set(null);
    try {
      const idem = newIdempotencyKey();
      await this.workItems.signal(p.id, wi.id, wi.currentCheckpointKey, { outcome: action.outcome, payload: action.payload }, idem);
      await this.refetchOnly(p.id, wi.id);
    } catch (e: unknown) {
      this.submitError.set(toAppError(e, 'Could not submit signal'));
    } finally {
      this.submitting.set(null);
    }
  }

  protected async onAssignmentSubmitted(action: AssignmentConfirmSubmit): Promise<void> {
    const p = this.project();
    const wi = this.workItem();
    if (!p || !wi || !wi.currentCheckpointKey) return;
    this.submitting.set(action.outcome);
    this.submitError.set(null);
    try {
      const idem = newIdempotencyKey();
      await this.workItems.signal(
        p.id, wi.id, wi.currentCheckpointKey,
        { outcome: action.outcome, payload: action.payload, taskId: action.taskId ?? undefined },
        idem);
      await this.refetchOnly(p.id, wi.id);
    } catch (e: unknown) {
      this.submitError.set(toAppError(e, 'Could not submit assignment'));
    } finally {
      this.submitting.set(null);
    }
  }

  protected copyCorrelationId(): void {
    const err = this.submitError();
    const raw = err ? JSON.stringify(err) : '';
    void navigator.clipboard?.writeText(raw);
  }

  protected statusPillClass(status: string): string {
    switch (status) {
      case 'Running': return 'bg-sky-50 text-sky-700';
      case 'WaitingOnCheckpoint': return 'bg-amber-50 text-amber-700';
      case 'Completed': return 'bg-emerald-50 text-emerald-700';
      case 'Failed': return 'bg-red-50 text-red-700';
      case 'Cancelled': return 'bg-slate-100 text-slate-600';
      default: return 'bg-slate-100 text-slate-600';
    }
  }

  protected readonly streamUrl = computed(() => {
    const p = this.project();
    const wi = this.workItem();
    const token = this.auth.token();
    if (!p || !wi || !token) return null;
    return this.workItems.streamUrl(p.id, wi.id, token);
  });

  private classify(e: unknown): void {
    if (e instanceof HttpErrorResponse) {
      if (e.status === 404) { this.errorKind.set('not-found'); return; }
      if (e.status === 403) { this.errorKind.set('forbidden'); return; }
      this.error.set(e.error && typeof e.error === 'object' && 'title' in e.error
        ? e.error as AppError
        : { type: 'about:blank', title: 'Could not load work item', status: e.status, detail: e.message });
      this.errorKind.set('other');
      return;
    }
    this.error.set({
      type: 'about:blank',
      title: 'Could not load work item',
      status: 0,
      detail: e instanceof Error ? e.message : 'An unexpected error occurred.',
    });
    this.errorKind.set('other');
  }
}

function readExecutorSteps(state: unknown): ExecutorStep[] {
  if (state && typeof state === 'object' && 'steps' in (state as Record<string, unknown>)) {
    const v = (state as Record<string, unknown>)['steps'];
    if (Array.isArray(v)) return v as ExecutorStep[];
  }
  return [];
}

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return `idem-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function toAppError(err: unknown, fallbackTitle: string): AppError {
  if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object' && 'title' in err.error) {
    return err.error as AppError;
  }
  return {
    type: 'about:blank',
    title: fallbackTitle,
    status: 0,
    detail: err instanceof Error ? err.message : 'An unexpected error occurred.',
  };
}
