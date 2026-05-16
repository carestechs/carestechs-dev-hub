import { ChangeDetectionStrategy, Component, EventEmitter, computed, effect, input, Output, signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type {
  AddMembershipRequest,
  MemberDto,
  ProjectMembershipDto,
  RoleDto,
  UpdateMembershipRequest,
} from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../../shared';

@Component({
  selector: 'membership-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './membership-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MembershipFormModal {
  readonly open = input<boolean>(false);
  /** Membership being edited; null for the add flow. */
  readonly editing = input<ProjectMembershipDto | null>(null);
  /** Members eligible for add (Active only, not already in the project). Ignored on edit. */
  readonly assignableMembers = input<MemberDto[]>([]);
  readonly availableRoles = input<RoleDto[]>([]);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);

  @Output() readonly submitted = new EventEmitter<AddMembershipRequest | UpdateMembershipRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    memberId: new FormControl<string>('', { nonNullable: true, validators: [Validators.required] }),
    roleKeys: new FormArray<FormControl<boolean>>([], [requireAtLeastOne]),
  });

  protected readonly submittedFlag = signal(false);

  protected readonly memberDisplay = computed(() => this.editing()?.member.displayName ?? '');
  protected readonly memberEmail = computed(() => this.editing()?.member.email ?? '');

  constructor() {
    // Re-sync the role checkboxes whenever the available roles / editing target changes.
    effect(() => {
      if (!this.open()) return;

      const roles = this.availableRoles();
      const checked = new Set(this.editing()?.roles ?? []);

      while (this.form.controls.roleKeys.length) this.form.controls.roleKeys.removeAt(0);
      for (const role of roles) {
        this.form.controls.roleKeys.push(new FormControl<boolean>(checked.has(role.key), { nonNullable: true }));
      }

      const editing = this.editing();
      if (editing) {
        this.form.controls.memberId.setValue(editing.member.id);
        this.form.controls.memberId.disable();
      } else {
        this.form.controls.memberId.setValue('');
        this.form.controls.memberId.enable();
      }

      this.submittedFlag.set(false);
    });
  }

  protected get title(): string { return this.editing() ? 'Edit membership' : 'Add membership'; }
  protected get submitLabel(): string { return this.editing() ? 'Save' : 'Add'; }

  protected memberError(): string | null {
    const c = this.form.controls.memberId;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    return 'Pick a member.';
  }

  protected rolesError(): string | null {
    const arr = this.form.controls.roleKeys;
    if (!(arr.touched || this.submittedFlag()) || arr.valid) return null;
    return 'Select at least one role.';
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.form.controls.roleKeys.markAsTouched();
      return;
    }

    const roles = this.availableRoles();
    const selectedKeys = this.form.controls.roleKeys.controls
      .map((ctrl, i) => ctrl.value ? roles[i]?.key : null)
      .filter((k): k is string => !!k);

    const editing = this.editing();
    if (editing) {
      const req: UpdateMembershipRequest = { roleKeys: selectedKeys };
      this.submitted.emit(req);
    } else {
      const req: AddMembershipRequest = {
        memberId: this.form.controls.memberId.value,
        roleKeys: selectedKeys,
      };
      this.submitted.emit(req);
    }
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}

function requireAtLeastOne(arr: { value?: unknown }): { atLeastOne: true } | null {
  const values = (arr.value as boolean[] | undefined) ?? [];
  return values.some(Boolean) ? null : { atLeastOne: true };
}
