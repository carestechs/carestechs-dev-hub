import {
  ChangeDetectionStrategy,
  Component,
  effect,
  EventEmitter,
  input,
  Output,
  signal,
} from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import type { UpdateProjectRequest } from '../../core/api/workspace.types';
import type { AppError } from '../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../shared';

const REPO_PATTERN = /^[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+$/;

function repoValidator(c: AbstractControl): ValidationErrors | null {
  const v = (c.value ?? '') as string;
  if (v === '') return null;
  if (!REPO_PATTERN.test(v)) return { repoShape: true };
  if (v.endsWith('.git')) return { repoGitSuffix: true };
  return null;
}

function branchValidator(c: AbstractControl): ValidationErrors | null {
  const v = (c.value ?? '') as string;
  if (v === '') return null;
  if (v.startsWith('/')) return { branchLeadingSlash: true };
  if (v.includes('..')) return { branchDotDot: true };
  for (const ch of v) {
    const code = ch.charCodeAt(0);
    if (code < 0x20 || code === 0x7F) return { branchControlChar: true };
    if (/\s/.test(ch)) return { branchWhitespace: true };
  }
  return null;
}

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
    if (c.errors?.['maxlength']) return 'Repo is too long.';
    if (c.errors?.['repoShape']) return "Use 'owner/name' — no URL prefix, no whitespace, no leading slash.";
    if (c.errors?.['repoGitSuffix']) return "Drop the '.git' suffix.";
    return null;
  }
  protected defaultBranchError(): string | null {
    const c = this.form.controls.defaultBranch;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['maxlength']) return 'Branch is too long.';
    if (c.errors?.['branchLeadingSlash']) return "Branch must not start with '/'.";
    if (c.errors?.['branchDotDot']) return "Branch must not contain '..'.";
    if (c.errors?.['branchWhitespace']) return 'Branch must not contain whitespace.';
    if (c.errors?.['branchControlChar']) return 'Branch must not contain control characters.';
    return null;
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
