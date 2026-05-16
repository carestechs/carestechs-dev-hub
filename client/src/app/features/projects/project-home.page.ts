import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { WorkItemsService } from '../../core/api/work-items.service';
import type { StartWorkItemRequest, WorkItemSummaryDto } from '../../core/api/work-items.types';
import { WorkspaceService } from '../../core/api/workspace.service';
import type { PageMeta, PageRequest, ProjectDto } from '../../core/api/workspace.types';
import type { AppError } from '../../core/errors/app-error';
import { AppButton } from '../../shared';
import { StartWorkModal } from './work-items/start-work.modal';

type ErrorKind = 'forbidden' | 'not-found' | 'other';
type StatusFilter = 'All' | 'Running' | 'WaitingOnCheckpoint' | 'Completed' | 'Failed' | 'Cancelled';

const STATUS_FILTERS: readonly StatusFilter[] = [
  'All', 'Running', 'WaitingOnCheckpoint', 'Completed', 'Failed', 'Cancelled',
] as const;

@Component({
  selector: 'project-home-page',
  standalone: true,
  imports: [RouterLink, AppButton, StartWorkModal],
  templateUrl: './project-home.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectHomePage {
  private readonly ws = inject(WorkspaceService);
  private readonly workItems = inject(WorkItemsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly project = signal<ProjectDto | null>(null);
  protected readonly errorKind = signal<ErrorKind | null>(null);
  protected readonly error = signal<AppError | null>(null);

  protected readonly slug = computed(() => this.project()?.slug ?? '');

  // Work items state
  protected readonly items = signal<WorkItemSummaryDto[]>([]);
  protected readonly itemsLoading = signal(false);
  protected readonly itemsError = signal<AppError | null>(null);
  protected readonly itemsMeta = signal<PageMeta | null>(null);
  protected readonly itemsPage = signal<PageRequest>({ page: 1, pageSize: 20 });
  protected readonly statusFilter = signal<StatusFilter>('All');
  protected readonly waitingOnMe = signal(false);
  protected readonly statusFilters = STATUS_FILTERS;

  // Start modal state
  protected readonly modalOpen = signal(false);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(async params => {
      const slug = params.get('slug');
      if (!slug) return;
      await this.load(slug);
    });
  }

  private async load(slug: string): Promise<void> {
    this.loading.set(true);
    this.errorKind.set(null);
    this.error.set(null);
    this.project.set(null);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.project.set(project);
      await this.loadItems();
    } catch (e: unknown) {
      this.classify(e);
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadItems(): Promise<void> {
    const p = this.project();
    if (!p) return;
    this.itemsLoading.set(true);
    this.itemsError.set(null);
    try {
      const env = await this.workItems.list(p.id, {
        ...this.itemsPage(),
        status: this.statusFilter() === 'All' ? undefined : this.statusFilter(),
        waitingOnMe: this.waitingOnMe() ? true : undefined,
      });
      this.items.set(env.data);
      this.itemsMeta.set(env.meta);
    } catch (e: unknown) {
      this.itemsError.set(toAppError(e, 'Could not load work items'));
    } finally {
      this.itemsLoading.set(false);
    }
  }

  protected setStatusFilter(f: StatusFilter): void {
    if (this.statusFilter() === f) return;
    this.statusFilter.set(f);
    this.itemsPage.update(p => ({ ...p, page: 1 }));
    void this.loadItems();
  }

  protected toggleWaitingOnMe(): void {
    this.waitingOnMe.update(v => !v);
    this.itemsPage.update(p => ({ ...p, page: 1 }));
    void this.loadItems();
  }

  protected onPrevPage(): void {
    const meta = this.itemsMeta();
    if (!meta || meta.page <= 1) return;
    this.itemsPage.update(p => ({ ...p, page: meta.page - 1 }));
    void this.loadItems();
  }
  protected onNextPage(): void {
    const meta = this.itemsMeta();
    if (!meta) return;
    if (meta.page >= this.totalPages()) return;
    this.itemsPage.update(p => ({ ...p, page: meta.page + 1 }));
    void this.loadItems();
  }
  protected totalPages(): number {
    const meta = this.itemsMeta();
    if (!meta) return 1;
    return Math.max(1, Math.ceil(meta.totalCount / meta.pageSize));
  }

  protected onRowClick(item: WorkItemSummaryDto): void {
    const p = this.project();
    if (!p) return;
    void this.router.navigate(['/projects', p.slug, 'work-items', item.id]);
  }

  protected openStart(): void {
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected onStartCancelled(): void {
    if (this.modalWorking()) return;
    this.modalOpen.set(false);
  }
  protected async onStartSubmitted(req: StartWorkItemRequest): Promise<void> {
    const p = this.project();
    if (!p) return;
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      const dto = await this.workItems.start(p.id, req);
      this.modalOpen.set(false);
      await this.loadItems();
      void this.router.navigate(['/projects', p.slug, 'work-items', dto.id]);
    } catch (e: unknown) {
      this.modalError.set(toAppError(e, 'Could not start work'));
    } finally {
      this.modalWorking.set(false);
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
      if (e.error && typeof e.error === 'object' && 'title' in e.error) {
        this.error.set(e.error as AppError);
      } else {
        this.error.set({ type: 'about:blank', title: 'Could not load project', status: e.status, detail: e.message });
      }
      this.errorKind.set('other');
      return;
    }
    this.error.set({
      type: 'about:blank',
      title: 'Could not load project',
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
