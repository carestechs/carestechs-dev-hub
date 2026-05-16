import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type {
  CreateTeamRequest,
  PageMeta,
  PageRequest,
  TeamDto,
  UpdateTeamRequest,
} from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppTable, ConfirmDialog } from '../../../shared';
import type { ColumnDef, PageChange, SortChange } from '../../../shared';
import { TeamFormModal } from './team-form.modal';

@Component({
  selector: 'teams-page',
  standalone: true,
  imports: [AppButton, AppTable, ConfirmDialog, TeamFormModal],
  templateUrl: './teams.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamsPage {
  private readonly ws = inject(WorkspaceService);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);

  protected readonly page = signal<PageRequest>({ page: 1, pageSize: 20, sortBy: 'name', sortDir: 'asc' });

  // Modal state ----------------------------------------------------------
  protected readonly modalOpen = signal(false);
  protected readonly editing = signal<TeamDto | null>(null);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);

  // Delete state ---------------------------------------------------------
  protected readonly toDelete = signal<TeamDto | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<AppError | null>(null);

  // Table columns --------------------------------------------------------
  protected readonly columns: ColumnDef<TeamDto>[] = [
    { id: 'name',         header: 'Name',         sortable: true, cell: t => t.name },
    { id: 'description',  header: 'Description',  cell: t => t.description ?? '—' },
    { id: 'projectCount', header: 'Projects',     align: 'right', cell: t => t.projectCount },
    { id: 'createdAt',    header: 'Created',      sortable: true, cell: t => t.createdAt.slice(0, 10) },
  ];

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const env = await this.ws.listTeams(this.page());
      this.teams.set(env.data);
      this.meta.set(env.meta);
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load teams'));
    } finally {
      this.loading.set(false);
    }
  }

  // Sort + page --------------------------------------------------------
  protected onSort(e: SortChange): void {
    this.page.update(p => ({ ...p, sortBy: e.sortBy, sortDir: e.sortDir, page: 1 }));
    void this.load();
  }
  protected onPage(e: PageChange): void {
    this.page.update(p => ({ ...p, page: e.page, pageSize: e.pageSize }));
    void this.load();
  }

  // Create / edit ------------------------------------------------------
  protected openCreate(): void {
    this.editing.set(null);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected openEdit(t: TeamDto): void {
    this.editing.set(t);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected onModalCancelled(): void {
    if (this.modalWorking()) return;
    this.modalOpen.set(false);
  }
  protected async onModalSubmit(req: CreateTeamRequest | UpdateTeamRequest): Promise<void> {
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      const editing = this.editing();
      if (editing) {
        await this.ws.updateTeam(editing.id, req as UpdateTeamRequest);
      } else {
        await this.ws.createTeam(req as CreateTeamRequest);
      }
      this.modalOpen.set(false);
      await this.load();
    } catch (e: unknown) {
      this.modalError.set(toAppError(e, 'Could not save team'));
    } finally {
      this.modalWorking.set(false);
    }
  }

  // Delete -----------------------------------------------------------
  protected askDelete(t: TeamDto): void {
    this.toDelete.set(t);
    this.deleteError.set(null);
  }
  protected onDeleteCancel(): void {
    if (this.deleting()) return;
    this.toDelete.set(null);
  }
  protected async onDeleteConfirm(): Promise<void> {
    const t = this.toDelete();
    if (!t) return;
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await this.ws.deleteTeam(t.id);
      this.toDelete.set(null);
      await this.load();
    } catch (e: unknown) {
      this.deleteError.set(toAppError(e, 'Could not delete team'));
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
