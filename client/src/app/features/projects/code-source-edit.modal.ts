import {
  ChangeDetectionStrategy,
  Component,
  effect,
  EventEmitter,
  input,
  Output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type { UpdateProjectRequest } from '../../core/api/workspace.types';
import type { AppError } from '../../core/errors/app-error';
import { branchErrorMessage, branchValidator, repoErrorMessage, repoValidator } from '../../core/validation/code-source-validators';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../shared';

/**
 * Dedicated edit modal for the FEAT-008 code-source fields on a project.
 * Kept separate from project-form.modal (which is create-only) so the create
 * flow's required-field validation stays clean.
 */
@Component({
  selector: 'code-source-edit-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './code-source-edit.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CodeSourceEditModal {
  readonly open = input<boolean>(false);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);
  readonly initialRepo = input<string | null | undefined>(null);
  readonly initialDefaultBranch = input<string | null | undefined>(null);

  @Output() readonly submitted = new EventEmitter<UpdateProjectRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    repo: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.maxLength(140), repoValidator],
    }),
    defaultBranch: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.maxLength(200), branchValidator],
    }),
  });

  protected readonly submittedFlag = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.form.reset({
        repo: this.initialRepo() ?? '',
        defaultBranch: this.initialDefaultBranch() ?? '',
      });
      this.submittedFlag.set(false);
    });
  }

  protected repoError(): string | null {
    const c = this.form.controls.repo;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    return repoErrorMessage(c.errors);
  }
  protected defaultBranchError(): string | null {
    const c = this.form.controls.defaultBranch;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    return branchErrorMessage(c.errors);
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const { repo, defaultBranch } = this.form.getRawValue();
    // Empty-string submit means "leave the field as-is" on update (matches the backend's
    // null = unchanged semantic). We omit empty fields rather than send `""`.
    const body: UpdateProjectRequest = {};
    if (repo !== (this.initialRepo() ?? '')) body.repo = repo;
    if (defaultBranch !== (this.initialDefaultBranch() ?? '')) body.defaultBranch = defaultBranch;
    this.submitted.emit(body);
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}
