import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { WorkspaceService } from '../../core/api/workspace.service';
import type {
  CreateProjectRequest,
  PageMeta,
  ProjectDto,
  TeamDto,
} from '../../core/api/workspace.types';
import { ExecutorRegistryService } from '../../core/api/executor-registry.service';
import type { ExecutorBindingDto } from '../../core/api/executor-registry.types';
import { AuthService } from '../../core/auth/auth.service';
import type { AppError } from '../../core/errors/app-error';
import { AppButton, AppErrorBanner, EmptyState } from '../../shared';
import { ProjectCard } from './project-card';
import { ProjectFormModal } from './project-form.modal';
import { HttpErrorResponse } from '@angular/common/http';

interface Filters {
  teamId: string;
  projectType: string;
  q: string;
}

@Component({
  selector: 'project-list-page',
  standalone: true,
  imports: [ReactiveFormsModule, AppButton, AppErrorBanner, EmptyState, ProjectCard, ProjectFormModal],
  templateUrl: './project-list.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectListPage {
  private readonly ws = inject(WorkspaceService);
  private readonly registry = inject(ExecutorRegistryService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly isOperator = this.auth.isOperator;

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly projects = signal<ProjectDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);

  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly bindings = signal<ExecutorBindingDto[]>([]);

  // Create-project modal state.
  protected readonly createOpen = signal(false);
  protected readonly creating = signal(false);
  protected readonly createError = signal<AppError | null>(null);

  protected readonly filterForm = new FormGroup({
    teamId: new FormControl<string>('', { nonNullable: true }),
    projectType: new FormControl<string>('', { nonNullable: true }),
    q: new FormControl<string>('', { nonNullable: true }),
  });

  /** Snapshot of the current filter values; used by both load() and the template. */
  protected readonly filters = signal<Filters>({ teamId: '', projectType: '', q: '' });

  protected readonly hasAnyFilter = computed(() => {
    const f = this.filters();
    return !!f.teamId || !!f.projectType || !!f.q;
  });

  constructor() {
    // First load.
    void this.load();

    // Watch the team dropdown for the filter chip (also kicks off the first
    // teams fetch — keeps the dropdown wired even when the user has no projects yet).
    void this.loadTeams();

    // Debounce filter changes to avoid hammering the API while typing.
    this.filterForm.valueChanges
      .pipe(takeUntilDestroyed(), debounceTime(250), distinctUntilChanged((a, b) =>
        a.teamId === b.teamId && a.projectType === b.projectType && a.q === b.q))
      .subscribe(value => {
        this.filters.set({
          teamId: value.teamId ?? '',
          projectType: value.projectType ?? '',
          q: value.q ?? '',
        });
        void this.load();
      });
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const f = this.filters();
      const env = await this.ws.listProjects({
        sortBy: 'updatedAt',
        sortDir: 'desc',
        teamId: f.teamId || undefined,
        projectType: f.projectType || undefined,
      });
      this.projects.set(this.applyClientSearch(env.data, f.q));
      this.meta.set(env.meta);
    } catch (e: unknown) {
      this.error.set(this.toAppError(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadTeams(): Promise<void> {
    try {
      const env = await this.ws.listTeams({ pageSize: 100, sortBy: 'name', sortDir: 'asc' });
      this.teams.set(env.data);
    } catch {
      // Non-fatal: the dropdown stays empty if the call fails.
    }
  }

  protected onCardOpen(p: ProjectDto): void {
    void this.router.navigate(['/projects', p.slug]);
  }

  protected clearFilters(): void {
    this.filterForm.reset({ teamId: '', projectType: '', q: '' });
  }

  protected async openCreate(): Promise<void> {
    this.createError.set(null);
    this.createOpen.set(true);
    // Lazy-load bindings (and refresh teams) so the operator sees current options.
    try {
      const [bindingsEnv] = await Promise.all([
        this.registry.listBindings({ pageSize: 100 }),
        this.teams().length === 0 ? this.loadTeams() : Promise.resolve(),
      ]);
      this.bindings.set(bindingsEnv.data);
    } catch {
      // Non-fatal — the form will surface the empty-state hint.
    }
  }

  protected closeCreate(): void {
    if (this.creating()) return;
    this.createOpen.set(false);
    this.createError.set(null);
  }

  protected async submitCreate(req: CreateProjectRequest): Promise<void> {
    this.creating.set(true);
    this.createError.set(null);
    try {
      const created = await this.ws.createProject(req);
      this.createOpen.set(false);
      void this.router.navigate(['/projects', created.slug]);
    } catch (e: unknown) {
      this.createError.set(this.toAppError(e));
    } finally {
      this.creating.set(false);
    }
  }

  /** v1 client-side search until the API gains a q parameter. */
  private applyClientSearch(items: ProjectDto[], q: string): ProjectDto[] {
    if (!q) return items;
    const needle = q.trim().toLowerCase();
    if (!needle) return items;
    return items.filter(p =>
      p.name.toLowerCase().includes(needle) ||
      p.slug.toLowerCase().includes(needle) ||
      p.projectType.toLowerCase().includes(needle));
  }

  private toAppError(err: unknown): AppError {
    if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object' && 'title' in err.error) {
      return err.error as AppError;
    }
    return {
      type: 'about:blank',
      title: 'Could not load projects',
      status: 0,
      detail: err instanceof Error ? err.message : 'An unexpected error occurred.',
    };
  }
}
