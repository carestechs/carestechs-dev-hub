import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  effect,
  input,
  OnInit,
  Output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type { CreateProjectRequest, TeamDto } from '../../core/api/workspace.types';
import type { ExecutorBindingDto } from '../../core/api/executor-registry.types';
import type { AppError } from '../../core/errors/app-error';
import { branchErrorMessage, branchValidator } from '../../core/validation/code-source-validators';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../shared';
import { IntegrationsService } from '../../core/api/integrations.service';

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

const REPO_NAME_PATTERN = /^[a-z0-9][a-z0-9-]*$/;

@Component({
  selector: 'project-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './project-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectFormModal implements OnInit {
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
    defaultBranch: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.maxLength(200), branchValidator],
    }),
    repoName: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.maxLength(100), Validators.pattern(REPO_NAME_PATTERN)],
    }),
  });

  protected readonly submittedFlag = signal(false);
  protected readonly githubConfigured = signal(false);
  protected readonly createRepo = signal(false);
  private slugTouched = false;
  private repoNameTouched = false;

  constructor(private readonly integrations: IntegrationsService) {
    effect(() => {
      if (!this.open()) return;
      this.form.reset({
        name: '', slug: '', projectType: '', owningTeamId: '',
        description: '', defaultBranch: '', repoName: '',
      });
      this.submittedFlag.set(false);
      this.createRepo.set(false);
      this.slugTouched = false;
      this.repoNameTouched = false;
    });

    // Auto-derive slug from name until the user edits the slug directly.
    this.form.controls.name.valueChanges.subscribe(name => {
      if (!this.slugTouched)
        this.form.controls.slug.setValue(this.slugify(name), { emitEvent: false });
      if (!this.repoNameTouched)
        this.form.controls.repoName.setValue(this.slugifyRepo(name), { emitEvent: false });
    });
    this.form.controls.slug.valueChanges.subscribe(() => {
      if (this.form.controls.slug.dirty) this.slugTouched = true;
    });
    this.form.controls.repoName.valueChanges.subscribe(() => {
      if (this.form.controls.repoName.dirty) this.repoNameTouched = true;
    });
  }

  ngOnInit(): void {
    this.integrations.getGitHubStatus()
      .then(s => this.githubConfigured.set(s.configured))
      .catch(() => this.githubConfigured.set(false));
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
  protected repoNameError(): string | null {
    const c = this.form.controls.repoName;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['maxlength']) return 'Name is too long (max 100 characters).';
    if (c.errors?.['pattern']) return 'Use lowercase letters, digits, and hyphens only; must start with a letter or digit.';
    return null;
  }
  protected defaultBranchError(): string | null {
    const c = this.form.controls.defaultBranch;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    return branchErrorMessage(c.errors);
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const { name, slug, projectType, owningTeamId, description, defaultBranch, repoName } = this.form.getRawValue();
    this.submitted.emit({
      name,
      slug,
      projectType,
      owningTeamId,
      description: description || undefined,
      defaultBranch: defaultBranch || undefined,
      createGitHubRepo: this.createRepo() || undefined,
      repoName: this.createRepo() && repoName ? repoName : undefined,
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

  private slugifyRepo(s: string): string {
    return s
      .toLowerCase()
      .normalize('NFKD').replace(/[̀-ͯ]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 100);
  }
}
