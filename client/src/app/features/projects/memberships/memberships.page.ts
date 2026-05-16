import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type {
  AddMembershipRequest,
  MemberDto,
  ProjectDto,
  ProjectMembershipDto,
  RoleDto,
  UpdateMembershipRequest,
} from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppTable, ConfirmDialog } from '../../../shared';
import type { ColumnDef } from '../../../shared';
import { MembershipFormModal } from './membership-form.modal';

@Component({
  selector: 'memberships-page',
  standalone: true,
  imports: [RouterLink, AppButton, AppTable, ConfirmDialog, MembershipFormModal],
  templateUrl: './memberships.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MembershipsPage {
  private readonly ws = inject(WorkspaceService);
  private readonly route = inject(ActivatedRoute);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);

  protected readonly project = signal<ProjectDto | null>(null);
  protected readonly memberships = signal<ProjectMembershipDto[]>([]);
  protected readonly availableRoles = signal<RoleDto[]>([]);
  protected readonly allActiveMembers = signal<MemberDto[]>([]);

  /** Active members not already on this project — used by the Add modal. */
  protected readonly assignableMembers = computed(() => {
    const inProject = new Set(this.memberships().map(m => m.member.id));
    return this.allActiveMembers().filter(m => !inProject.has(m.id));
  });

  // Modal state ----------------------------------------------------------
  protected readonly modalOpen = signal(false);
  protected readonly editing = signal<ProjectMembershipDto | null>(null);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);

  // Remove state ---------------------------------------------------------
  protected readonly toRemove = signal<ProjectMembershipDto | null>(null);
  protected readonly removing = signal(false);
  protected readonly removeError = signal<AppError | null>(null);

  protected readonly columns: ColumnDef<ProjectMembershipDto>[] = [
    { id: 'member',    header: 'Member',    cell: m => m.member.displayName },
    { id: 'email',     header: 'Email',     cell: m => m.member.email },
    { id: 'roles',     header: 'Roles',     cell: m => m.roles.join(', ') },
    { id: 'createdAt', header: 'Added',     cell: m => m.createdAt.slice(0, 10) },
  ];

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(async params => {
      const slug = params.get('slug');
      if (!slug) return;
      await this.loadAll(slug);
    });
  }

  private async loadAll(slug: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.project.set(project);

      const [memberships, roles, allMembersEnv] = await Promise.all([
        this.ws.listMemberships(project.id),
        this.ws.listRoles(),
        this.ws.listMembers({ pageSize: 200, sortBy: 'displayName', sortDir: 'asc' }),
      ]);
      this.memberships.set(memberships);
      this.availableRoles.set(roles);
      this.allActiveMembers.set(allMembersEnv.data.filter(m => m.status === 'Active'));
    } catch (e: unknown) {
      this.error.set(toAppError(e, 'Could not load memberships'));
    } finally {
      this.loading.set(false);
    }
  }

  private async refreshMemberships(): Promise<void> {
    const p = this.project();
    if (!p) return;
    try {
      this.memberships.set(await this.ws.listMemberships(p.id));
    } catch {
      // Refresh is best-effort; an error gets surfaced by the next user action.
    }
  }

  protected openAdd(): void {
    this.editing.set(null);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected openEdit(m: ProjectMembershipDto): void {
    this.editing.set(m);
    this.modalError.set(null);
    this.modalOpen.set(true);
  }
  protected onModalCancelled(): void {
    if (this.modalWorking()) return;
    this.modalOpen.set(false);
  }
  protected async onModalSubmit(req: AddMembershipRequest | UpdateMembershipRequest): Promise<void> {
    const p = this.project();
    if (!p) return;
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      const editing = this.editing();
      if (editing) {
        await this.ws.updateMembership(p.id, editing.id, req as UpdateMembershipRequest);
      } else {
        await this.ws.addMembership(p.id, req as AddMembershipRequest);
      }
      this.modalOpen.set(false);
      await this.refreshMemberships();
    } catch (e: unknown) {
      this.modalError.set(toAppError(e, 'Could not save membership'));
    } finally {
      this.modalWorking.set(false);
    }
  }

  protected askRemove(m: ProjectMembershipDto): void {
    this.toRemove.set(m);
    this.removeError.set(null);
  }
  protected onRemoveCancel(): void {
    if (this.removing()) return;
    this.toRemove.set(null);
  }
  protected async onRemoveConfirm(): Promise<void> {
    const p = this.project();
    const m = this.toRemove();
    if (!p || !m) return;
    this.removing.set(true);
    this.removeError.set(null);
    try {
      await this.ws.removeMembership(p.id, m.id);
      this.toRemove.set(null);
      await this.refreshMemberships();
    } catch (e: unknown) {
      this.removeError.set(toAppError(e, 'Could not remove membership'));
    } finally {
      this.removing.set(false);
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
