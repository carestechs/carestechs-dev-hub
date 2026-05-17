import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  effect,
  input,
  Output,
  signal,
} from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import type { CreateProjectRequest, TeamDto } from '../../core/api/workspace.types';
import type { ExecutorBindingDto } from '../../core/api/executor-registry.types';
import type { AppError } from '../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../shared';

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const REPO_PATTERN = /^[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+$/;

/** Mirrors CodeSourceValidator.ValidateRepo in DevHub.Contracts (FEAT-008 / T-056). */
function repoValidator(c: AbstractControl): ValidationErrors | null {
  const v = (c.value ?? '') as string;
  if (v === '') return null;  // optional
  if (!REPO_PATTERN.test(v)) return { repoShape: true };
  if (v.endsWith('.git')) return { repoGitSuffix: true };
  return null;
}

/** Mirrors CodeSourceValidator.ValidateBranch in DevHub.Contracts (FEAT-008 / T-056). */
function branchValidator(c: AbstractControl): ValidationErrors | null {
  const v = (c.value ?? '') as string;
  if (v === '') return null;  // optional
  if (v.startsWith('/')) return { branchLeadingSlash: true };
  if (v.includes('..')) return { branchDotDot: true };
  for (const ch of v) {
    const code = ch.charCodeAt(0);
    if (code < 0x20 || code === 0x7F) return { branchControlChar: true };
    if (/\s/.test(ch)) return { branchWhitespace: true };
  }
  return null;
}

@Component({
  selector: 'project-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './project-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectFormModal {
  readonly open = input<boolean>(false);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);
  readonly teams = input<TeamDto[]>([]);
  readonly bindings = input<ExecutorBindingDto[]>([]);

  @Output() readonly submitted = new EventEmitter<CreateProjectRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    name: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    slug: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(80), Validators.pattern(SLUG_PATTERN)],
    }),
    projectType: new FormControl<string>('', { nonNullable: true, validators: [Validators.required] }),
    owningTeamId: new FormControl<string>('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl<string>('', { nonNullable: true }),
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
  private slugTouched = false;

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.form.reset({
        name: '', slug: '', projectType: '', owningTeamId: '',
        description: '', repo: '', defaultBranch: '',
      });
      this.submittedFlag.set(false);
      this.slugTouched = false;
    });

    // Auto-derive slug from name until the user edits the slug directly.
    this.form.controls.name.valueChanges.subscribe(name => {
      if (this.slugTouched) return;
      this.form.controls.slug.setValue(this.slugify(name), { emitEvent: false });
    });
    this.form.controls.slug.valueChanges.subscribe(() => {
      if (this.form.controls.slug.dirty) this.slugTouched = true;
    });
  }

  protected nameError(): string | null {
    const c = this.form.controls.name;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Name is required.';
    if (c.errors?.['maxlength']) return 'Name is too long.';
    return null;
  }
  protected slugError(): string | null {
    const c = this.form.controls.slug;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Slug is required.';
    if (c.errors?.['maxlength']) return 'Slug is too long.';
    if (c.errors?.['pattern']) return 'Use lowercase letters, digits, and dashes only.';
    return null;
  }
  protected typeError(): string | null {
    const c = this.form.controls.projectType;
    return (c.touched || this.submittedFlag()) && c.invalid ? 'Project type is required.' : null;
  }
  protected teamError(): string | null {
    const c = this.form.controls.owningTeamId;
    return (c.touched || this.submittedFlag()) && c.invalid ? 'Owning team is required.' : null;
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
    const { name, slug, projectType, owningTeamId, description, repo, defaultBranch } = this.form.getRawValue();
    this.submitted.emit({
      name,
      slug,
      projectType,
      owningTeamId,
      description: description || undefined,
      repo: repo || undefined,
      defaultBranch: defaultBranch || undefined,
    });
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }

  private slugify(s: string): string {
    return s
      .toLowerCase()
      .normalize('NFKD').replace(/[̀-ͯ]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 80);
  }
}
