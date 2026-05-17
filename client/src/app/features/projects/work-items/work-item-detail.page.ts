import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { WorkItemsService } from '../../../core/api/work-items.service';
import type {
  CheckpointSignalDto,
  WorkItemDto,
} from '../../../core/api/work-items.types';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type { ProjectDto } from '../../../core/api/workspace.types';
import { branchErrorMessage, branchValidator } from '../../../core/validation/code-source-validators';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, ConfirmDialog } from '../../../shared';
import { ExecutorStatePanel } from './components/executor-state-panel';
import { SignalHistoryList } from './components/signal-history-list';
import { StreamFeed } from './components/stream-feed';

type ErrorKind = 'forbidden' | 'not-found' | 'other';
const REVIEW_EXECUTOR_KEYS = new Set<string>(['feature-delivery-v1']);
const SIGNAL_PAGE_SIZE = 20;

@Component({
  selector: 'work-item-detail-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AppButton,
    ConfirmDialog,
    ExecutorStatePanel,
    SignalHistoryList,
    StreamFeed,
  ],
  templateUrl: './work-item-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkItemDetailPage {
  private readonly ws = inject(WorkspaceService);
  private readonly workItems = inject(WorkItemsService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly project = signal<ProjectDto | null>(null);
  protected readonly workItem = signal<WorkItemDto | null>(null);
  protected readonly signals = signal<CheckpointSignalDto[]>([]);
  protected readonly signalsLoading = signal(false);
  protected readonly signalsTotal = signal(0);
  protected readonly signalsPage = signal(1);

  protected readonly errorKind = signal<ErrorKind | null>(null);
  protected readonly error = signal<AppError | null>(null);

  protected readonly toCancel = signal<WorkItemDto | null>(null);
  protected readonly cancelling = signal(false);
  protected readonly cancelError = signal<AppError | null>(null);

  // Inline workBranch edit state (operator-only).
  protected readonly branchEditOpen = signal(false);
  protected readonly branchSaving = signal(false);
  protected readonly branchError = signal<AppError | null>(null);
  protected readonly branchControl = new FormControl<string>('', {
    nonNullable: true,
    validators: [Validators.maxLength(200), branchValidator],
  });
  protected readonly isOperator = this.auth.isOperator;

  /** workBranch (override) → project.defaultBranch (project default) → null (not set). */
  protected readonly effectiveBranch = computed<{ value: string | null; source: 'override' | 'project-default' | 'unset' }>(() => {
    const wi = this.workItem();
    const p = this.project();
    if (wi?.workBranch) return { value: wi.workBranch, source: 'override' };
    if (p?.defaultBranch) return { value: p.defaultBranch, source: 'project-default' };
    return { value: null, source: 'unset' };
  });

  protected readonly streamUrl = computed(() => {
    const p = this.project();
    const wi = this.workItem();
    const token = this.auth.token();
    if (!p || !wi || !token) return null;
    return this.workItems.streamUrl(p.id, wi.id, token);
  });

  protected readonly canOpenReview = computed(() => {
    const wi = this.workItem();
    return wi !== null && REVIEW_EXECUTOR_KEYS.has(wi.executor.key);
  });

  protected readonly canLoadMoreSignals = computed(() =>
    this.signals().length < this.signalsTotal());

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
    this.signalsPage.set(1);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.project.set(project);
      await Promise.all([
        this.workItems.get(project.id, workItemId)
          .then(wi => this.workItem.set(wi)),
        this.loadSignalsPage(project.id, workItemId, 1),
      ]);
    } catch (e: unknown) {
      this.classify(e);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadSignalsPage(projectId: string, workItemId: string, page: number): Promise<void> {
    this.signalsLoading.set(true);
    try {
      const env = await this.workItems.listSignals(projectId, workItemId, { page, pageSize: SIGNAL_PAGE_SIZE });
      this.signals.update(current => page === 1 ? env.data : current.concat(env.data));
      this.signalsTotal.set(env.meta.totalCount);
      this.signalsPage.set(page);
    } finally {
      this.signalsLoading.set(false);
    }
  }

  protected async onLoadMoreSignals(): Promise<void> {
    const p = this.project(); const wi = this.workItem();
    if (!p || !wi) return;
    await this.loadSignalsPage(p.id, wi.id, this.signalsPage() + 1);
  }

  protected onCheckpointResolved(): void {
    const p = this.project(); const wi = this.workItem();
    if (!p || !wi) return;
    // Refetch the work item to refresh status + checkpoint.
    void this.workItems.get(p.id, wi.id).then(refreshed => this.workItem.set(refreshed));
  }

  protected openReview(): void {
    const p = this.project(); const wi = this.workItem();
    if (!p || !wi) return;
    void this.router.navigate(['/projects', p.slug, 'work-items', wi.id, 'review']);
  }

  protected askCancel(): void {
    this.toCancel.set(this.workItem());
    this.cancelError.set(null);
  }
  protected onCancelDismiss(): void {
    if (this.cancelling()) return;
    this.toCancel.set(null);
  }
  protected async onCancelConfirm(): Promise<void> {
    const p = this.project(); const wi = this.toCancel();
    if (!p || !wi) return;
    this.cancelling.set(true);
    this.cancelError.set(null);
    try {
      await this.workItems.cancel(p.id, wi.id);
      this.toCancel.set(null);
      // Refetch.
      this.workItems.get(p.id, wi.id).then(refreshed => this.workItem.set(refreshed)).catch(() => { });
    } catch (e: unknown) {
      this.cancelError.set(toAppError(e, 'Could not cancel work item'));
    } finally {
      this.cancelling.set(false);
    }
  }

  protected openBranchEdit(): void {
    if (!this.isOperator()) return;
    const wi = this.workItem();
    this.branchControl.reset(wi?.workBranch ?? '');
    this.branchError.set(null);
    this.branchEditOpen.set(true);
  }
  protected closeBranchEdit(): void {
    if (this.branchSaving()) return;
    this.branchEditOpen.set(false);
  }
  protected branchInputError(): string | null {
    const c = this.branchControl;
    if (!(c.touched || c.dirty) || c.valid) return null;
    return branchErrorMessage(c.errors);
  }
  protected async submitBranch(): Promise<void> {
    const p = this.project();
    const wi = this.workItem();
    if (!p || !wi) return;
    this.branchControl.markAsTouched();
    if (this.branchControl.invalid) return;

    const value = this.branchControl.value;
    const wasNull = wi.workBranch === null || wi.workBranch === undefined;
    if (value === '' && wasNull) {
      // No change — close without an API call.
      this.branchEditOpen.set(false);
      return;
    }
    if (value === (wi.workBranch ?? '')) {
      this.branchEditOpen.set(false);
      return;
    }

    this.branchSaving.set(true);
    this.branchError.set(null);
    try {
      // Empty string means "clear the override" on the backend (T-058 convention).
      const updated = await this.workItems.update(p.id, wi.id, { workBranch: value });
      this.workItem.set(updated);
      this.branchEditOpen.set(false);
    } catch (e: unknown) {
      this.branchError.set(toAppError(e, 'Could not update branch'));
    } finally {
      this.branchSaving.set(false);
    }
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
