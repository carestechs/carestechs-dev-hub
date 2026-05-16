import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type {
  InviteMemberRequest,
  MemberDto,
  PageMeta,
  PageRequest,
  UpdateMemberRequest,
} from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppTable, ConfirmDialog } from '../../../shared';
import type { ColumnDef, PageChange, SortChange } from '../../../shared';
import { MemberFormModal } from './member-form.modal';

@Component({
  selector: 'members-page',
  standalone: true,
  imports: [ReactiveFormsModule, AppButton, AppTable, ConfirmDialog, MemberFormModal],
  templateUrl: './members.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MembersPage {
  private readonly ws = inject(WorkspaceService);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly members = signal<MemberDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);

  protected readonly page = signal<PageRequest>({ page: 1, pageSize: 20, sortBy: 'displayName', sortDir: 'asc' });
  protected readonly q = signal<string>('');

  protected readonly search = new FormControl<string>('', { nonNullable: true });

  // Modal state ----------------------------------------------------------
  protected readonly modalOpen = signal(false);
  protected readonly editing = signal<MemberDto | null>(null);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);

  // Delete state ---------------------------------------------------------
  protected readonly toDelete = signal<MemberDto | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<AppError | null>(null);

  protected readonly columns: ColumnDef<MemberDto>[] = [
    { id: 'displayName', header: 'Display name', sortable: true, cell: m => m.displayName },
    { id: 'email',       header: 'Email',        sortable: true, cell: m => m.email },
    { id: 'status',      header: 'Status',       cell: m => m.status },
    { id: 'createdAt',   header: 'Created',      sortable: true, cell: m => m.createdAt.slice(0, 10) },
  ];

  constructor() {
    void this.load();
    this.search.valueChanges
      .pipe(takeUntilDestroyed(), debounceTime(250), distinctUntilChanged())
      .subscribe(v => {
        this.q.set(v);
        this.page.update(p => ({ ...p, page: 1 }));
        void this.load();
      });
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const env = await this.ws.listMembers({ ...this.page(), q: this.q() || undefined });
      this.members.set(env.data);
      this.meta.set(env.meta);
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load members'));
    } finally {
      this.loading.set(false);
    }
  }

  protected onSort(e: SortChange): void {
    this.page.update(p => ({ ...p, sortBy: e.sortBy, sortDir: e.sortDir, page: 1 }));
    void this.load();
  }
  protected onPage(e: PageChange): void {
    this.page.update(p => ({ ...p, page: e.page, pageSize: e.pageSize }));
    void this.load();
  }

  protected openInvite(): void {
    this.editing.set(null);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected openEdit(m: MemberDto): void {
    this.editing.set(m);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected onModalCancelled(): void {
    if (this.modalWorking()) return;
    this.modalOpen.set(false);
  }
  protected async onModalSubmit(req: InviteMemberRequest | UpdateMemberRequest): Promise<void> {
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      const editing = this.editing();
      if (editing) {
        await this.ws.updateMember(editing.id, req as UpdateMemberRequest);
      } else {
        await this.ws.inviteMember(req as InviteMemberRequest);
      }
      this.modalOpen.set(false);
      await this.load();
    } catch (e: unknown) {
      this.modalError.set(toAppError(e, 'Could not save member'));
    } finally {
      this.modalWorking.set(false);
    }
  }

  protected askDelete(m: MemberDto): void {
    this.toDelete.set(m);
    this.deleteError.set(null);
  }
  protected onDeleteCancel(): void {
    if (this.deleting()) return;
    this.toDelete.set(null);
  }
  protected async onDeleteConfirm(): Promise<void> {
    const m = this.toDelete();
    if (!m) return;
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await this.ws.deleteMember(m.id);
      this.toDelete.set(null);
      await this.load();
    } catch (e: unknown) {
      this.deleteError.set(toAppError(e, 'Could not delete member'));
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
