import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ExecutorRegistryService } from '../../../core/api/executor-registry.service';
import type {
  CreateExecutorRequest,
  ExecutorDto,
  ReplaceContractsRequest,
  UpdateExecutorRequest,
} from '../../../core/api/executor-registry.types';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type { PageMeta, PageRequest, RoleDto } from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppTable, ConfirmDialog } from '../../../shared';
import type { ColumnDef, PageChange } from '../../../shared';
import { ContractsFormModal } from './contracts-form.modal';
import { ExecutorFormModal } from './executor-form.modal';

@Component({
  selector: 'executors-page',
  standalone: true,
  imports: [AppButton, AppTable, ConfirmDialog, ExecutorFormModal, ContractsFormModal],
  templateUrl: './executors.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExecutorsPage {
  private readonly registry = inject(ExecutorRegistryService);
  private readonly ws = inject(WorkspaceService);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly executors = signal<ExecutorDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);
  protected readonly roles = signal<RoleDto[]>([]);

  protected readonly page = signal<PageRequest>({ page: 1, pageSize: 20 });

  // Executor modal -------------------------------------------------------
  protected readonly modalOpen = signal(false);
  protected readonly editing = signal<ExecutorDto | null>(null);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);

  // Contracts modal ------------------------------------------------------
  protected readonly contractsOpen = signal(false);
  protected readonly contractsTarget = signal<ExecutorDto | null>(null);
  protected readonly contractsWorking = signal(false);
  protected readonly contractsError = signal<AppError | null>(null);

  // Delete confirm ------------------------------------------------------
  protected readonly toDelete = signal<ExecutorDto | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<AppError | null>(null);

  protected readonly columns: ColumnDef<ExecutorDto>[] = [
    { id: 'key',          header: 'Key',          cell: e => e.key },
    { id: 'displayName',  header: 'Display name', cell: e => e.displayName },
    { id: 'baseUrl',      header: 'Base URL',     cell: e => e.baseUrl },
    { id: 'protocol',     header: 'Protocol',     cell: e => e.protocol },
    { id: 'status',       header: 'Status',       cell: e => e.status },
    { id: 'contracts',    header: 'Contracts',    align: 'right', cell: e => e.checkpointContracts.length },
    { id: 'createdAt',    header: 'Created',      cell: e => e.createdAt.slice(0, 10) },
  ];

  constructor() {
    void this.bootstrap();
  }

  protected async bootstrap(): Promise<void> {
    try {
      const [list, roles] = await Promise.all([
        this.registry.listExecutors(this.page()),
        this.ws.listRoles(),
      ]);
      this.executors.set(list.data);
      this.meta.set(list.meta);
      this.roles.set(roles);
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load executors'));
    } finally {
      this.loading.set(false);
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const list = await this.registry.listExecutors(this.page());
      this.executors.set(list.data);
      this.meta.set(list.meta);
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load executors'));
    } finally {
      this.loading.set(false);
    }
  }

  protected onPage(e: PageChange): void {
    this.page.update(p => ({ ...p, page: e.page, pageSize: e.pageSize }));
    void this.load();
  }

  // Register / edit ------------------------------------------------------
  protected openRegister(): void {
    this.editing.set(null);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected openEdit(e: ExecutorDto): void {
    this.editing.set(e);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected onModalCancelled(): void {
    if (this.modalWorking()) return;
    this.modalOpen.set(false);
  }
  protected async onModalSubmit(req: CreateExecutorRequest | UpdateExecutorRequest): Promise<void> {
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      const editing = this.editing();
      if (editing) {
        await this.registry.updateExecutor(editing.id, req as UpdateExecutorRequest);
      } else {
        await this.registry.createExecutor(req as CreateExecutorRequest);
      }
      this.modalOpen.set(false);
      await this.load();
    } catch (err: unknown) {
      this.modalError.set(toAppError(err, 'Could not save executor'));
    } finally {
      this.modalWorking.set(false);
    }
  }

  // Replace contracts ----------------------------------------------------
  protected openReplaceContracts(e: ExecutorDto): void {
    this.contractsTarget.set(e);
    this.contractsError.set(null);
    this.contractsOpen.set(true);
  }
  protected onContractsCancelled(): void {
    if (this.contractsWorking()) return;
    this.contractsOpen.set(false);
  }
  protected async onContractsSubmit(req: ReplaceContractsRequest): Promise<void> {
    const target = this.contractsTarget();
    if (!target) return;
    this.contractsWorking.set(true);
    this.contractsError.set(null);
    try {
      await this.registry.replaceContracts(target.id, req);
      this.contractsOpen.set(false);
      await this.load();
    } catch (err: unknown) {
      this.contractsError.set(toAppError(err, 'Could not replace contracts'));
    } finally {
      this.contractsWorking.set(false);
    }
  }

  // Delete ---------------------------------------------------------------
  protected askDelete(e: ExecutorDto): void {
    this.toDelete.set(e);
    this.deleteError.set(null);
  }
  protected onDeleteCancel(): void {
    if (this.deleting()) return;
    this.toDelete.set(null);
  }
  protected async onDeleteConfirm(): Promise<void> {
    const e = this.toDelete();
    if (!e) return;
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await this.registry.deleteExecutor(e.id);
      this.toDelete.set(null);
      await this.load();
    } catch (err: unknown) {
      this.deleteError.set(toAppError(err, 'Could not delete executor'));
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
