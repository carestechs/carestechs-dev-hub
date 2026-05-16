import { ChangeDetectionStrategy, Component, EventEmitter, effect, input, Output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type {
  InviteMemberRequest,
  MemberDto,
  MemberStatus,
  UpdateMemberRequest,
} from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../../shared';

@Component({
  selector: 'member-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './member-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MemberFormModal {
  readonly open = input<boolean>(false);
  readonly editing = input<MemberDto | null>(null);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);

  @Output() readonly submitted = new EventEmitter<InviteMemberRequest | UpdateMemberRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    displayName: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    email: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(255)],
    }),
    status: new FormControl<MemberStatus>('Active', { nonNullable: true }),
  });

  protected readonly submittedFlag = signal(false);

  protected readonly statusOptions: readonly MemberStatus[] = ['Active', 'Suspended', 'Invited'] as const;

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const m = this.editing();
      this.form.reset({
        displayName: m?.displayName ?? '',
        email: m?.email ?? '',
        status: m?.status ?? 'Active',
      });
      // Email is immutable on edit (used as the natural key).
      if (m) this.form.controls.email.disable(); else this.form.controls.email.enable();
      this.submittedFlag.set(false);
    });
  }

  protected get title(): string { return this.editing() ? 'Edit member' : 'Invite member'; }
  protected get submitLabel(): string { return this.editing() ? 'Save' : 'Invite'; }

  protected displayNameError(): string | null {
    const c = this.form.controls.displayName;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Display name is required.';
    if (c.errors?.['maxlength']) return 'Display name is too long.';
    return null;
  }

  protected emailError(): string | null {
    const c = this.form.controls.email;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Email is required.';
    if (c.errors?.['email']) return 'Enter a valid email.';
    if (c.errors?.['maxlength']) return 'Email is too long.';
    return null;
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const v = this.form.getRawValue();
    if (this.editing()) {
      const req: UpdateMemberRequest = { displayName: v.displayName, status: v.status };
      this.submitted.emit(req);
    } else {
      const req: InviteMemberRequest = { displayName: v.displayName, email: v.email };
      this.submitted.emit(req);
    }
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}
