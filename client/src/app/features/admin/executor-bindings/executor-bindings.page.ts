import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExecutorRegistryService } from '../../../core/api/executor-registry.service';
import type {
  CreateBindingRequest,
  ExecutorBindingDto,
  ExecutorDto,
} from '../../../core/api/executor-registry.types';
import type { PageMeta, PageRequest } from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppTable, ConfirmDialog } from '../../../shared';
import type { ColumnDef, PageChange } from '../../../shared';
import { BindingFormModal } from './binding-form.modal';

@Component({
  selector: 'executor-bindings-page',
  standalone: true,
  imports: [RouterLink, AppButton, AppTable, ConfirmDialog, BindingFormModal],
  templateUrl: './executor-bindings.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExecutorBindingsPage {
  private readonly registry = inject(ExecutorRegistryService);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly bindings = signal<ExecutorBindingDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);
  protected readonly executors = signal<ExecutorDto[]>([]);

  protected readonly page = signal<PageRequest>({ page: 1, pageSize: 20 });

  // Modal state ---------------------------------------------------------
  protected readonly modalOpen = signal(false);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);

  // Delete state --------------------------------------------------------
  protected readonly toDelete = signal<ExecutorBindingDto | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<AppError | null>(null);

  protected readonly columns: ColumnDef<ExecutorBindingDto>[] = [
    { id: 'projectType',  header: 'Project type', cell: b => b.projectType },
    { id: 'executor',     header: 'Executor',     cell: b => `${b.executorDisplayName} (${b.executorKey})` },
    { id: 'status',       header: 'Status',       cell: b => b.executorStatus },
    { id: 'createdAt',    header: 'Created',      cell: b => b.createdAt.slice(0, 10) },
  ];

  constructor() {
    void this.bootstrap();
  }

  protected async bootstrap(): Promise<void> {
    try {
      const [list, executors] = await Promise.all([
        this.registry.listBindings(this.page()),
        this.registry.listExecutors({ pageSize: 200 }),
      ]);
      this.bindings.set(list.data);
      this.meta.set(list.meta);
      this.executors.set(executors.data);
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load bindings'));
    } finally {
      this.loading.set(false);
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const list = await this.registry.listBindings(this.page());
      this.bindings.set(list.data);
      this.meta.set(list.meta);
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load bindings'));
    } finally {
      this.loading.set(false);
    }
  }

  protected onPage(e: PageChange): void {
    this.page.update(p => ({ ...p, page: e.page, pageSize: e.pageSize }));
    void this.load();
  }

  // Create -------------------------------------------------------------
  protected openNew(): void {
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected onModalCancelled(): void {
    if (this.modalWorking()) return;
    this.modalOpen.set(false);
  }
  protected async onModalSubmit(req: CreateBindingRequest): Promise<void> {
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      await this.registry.createBinding(req);
      this.modalOpen.set(false);
      await this.load();
    } catch (e: unknown) {
      this.modalError.set(toAppError(e, 'Could not create binding'));
    } finally {
      this.modalWorking.set(false);
    }
  }

  // Delete -------------------------------------------------------------
  protected askDelete(b: ExecutorBindingDto): void {
    this.toDelete.set(b);
    this.deleteError.set(null);
  }
  protected onDeleteCancel(): void {
    if (this.deleting()) return;
    this.toDelete.set(null);
  }
  protected async onDeleteConfirm(): Promise<void> {
    const b = this.toDelete();
    if (!b) return;
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await this.registry.deleteBinding(b.id);
      this.toDelete.set(null);
      await this.load();
    } catch (e: unknown) {
      this.deleteError.set(toAppError(e, 'Could not delete binding'));
    } finally {
      this.deleting.set(false);
    }
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
