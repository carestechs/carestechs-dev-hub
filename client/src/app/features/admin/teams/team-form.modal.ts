import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  effect,
  input,
  Output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type {
  CreateTeamRequest,
  TeamDto,
  UpdateTeamRequest,
} from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../../shared';

@Component({
  selector: 'team-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './team-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamFormModal {
  readonly open = input<boolean>(false);
  readonly editing = input<TeamDto | null>(null);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);

  @Output() readonly submitted = new EventEmitter<CreateTeamRequest | UpdateTeamRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    name: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    description: new FormControl<string>('', { nonNullable: true }),
  });

  protected readonly submittedFlag = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const t = this.editing();
      this.form.reset({ name: t?.name ?? '', description: t?.description ?? '' });
      this.submittedFlag.set(false);
    });
  }

  protected get title(): string { return this.editing() ? 'Edit team' : 'New team'; }
  protected get submitLabel(): string { return this.editing() ? 'Save' : 'Create'; }

  protected nameError(): string | null {
    const c = this.form.controls.name;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Name is required.';
    if (c.errors?.['maxlength']) return 'Name is too long.';
    return null;
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const { name, description } = this.form.getRawValue();
    this.submitted.emit({ name, description: description || undefined });
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}
