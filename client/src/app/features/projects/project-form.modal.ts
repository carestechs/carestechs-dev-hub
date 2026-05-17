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
import type { CreateProjectRequest, TeamDto } from '../../core/api/workspace.types';
import type { ExecutorBindingDto } from '../../core/api/executor-registry.types';
import type { AppError } from '../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../shared';

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

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
  });

  protected readonly submittedFlag = signal(false);
  private slugTouched = false;

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.form.reset({ name: '', slug: '', projectType: '', owningTeamId: '', description: '' });
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

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const { name, slug, projectType, owningTeamId, description } = this.form.getRawValue();
    this.submitted.emit({
      name,
      slug,
      projectType,
      owningTeamId,
      description: description || undefined,
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
