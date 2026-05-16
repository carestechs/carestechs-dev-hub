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
  CreateBindingRequest,
  ExecutorDto,
} from '../../../core/api/executor-registry.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../../shared';

@Component({
  selector: 'binding-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './binding-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BindingFormModal {
  readonly open = input<boolean>(false);
  readonly executors = input<ExecutorDto[]>([]);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);

  @Output() readonly submitted = new EventEmitter<CreateBindingRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    projectType: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(60), Validators.pattern(/^[a-z0-9-]+$/)],
    }),
    executorId: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected readonly submittedFlag = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.form.reset({ projectType: '', executorId: '' });
      this.submittedFlag.set(false);
    });
  }

  protected projectTypeError(): string | null {
    const c = this.form.controls.projectType;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Project type is required.';
    if (c.errors?.['pattern']) return 'Use lowercase letters, digits, and hyphens only.';
    if (c.errors?.['maxlength']) return 'Project type is too long.';
    return null;
  }

  protected executorError(): string | null {
    const c = this.form.controls.executorId;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Pick an executor.';
    return null;
  }

  protected get hasActiveExecutors(): boolean {
    return this.executors().some(e => e.status === 'Active');
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const { projectType, executorId } = this.form.getRawValue();
    this.submitted.emit({ projectType, executorId });
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}
