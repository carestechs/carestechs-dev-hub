import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WorkspaceService } from '../../core/api/workspace.service';
import type { ProjectDto } from '../../core/api/workspace.types';
import type { AppError } from '../../core/errors/app-error';
import { AppCard, EmptyState } from '../../shared';

type ErrorKind = 'forbidden' | 'not-found' | 'other';

@Component({
  selector: 'project-home-page',
  standalone: true,
  imports: [RouterLink, AppCard, EmptyState],
  templateUrl: './project-home.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectHomePage {
  private readonly ws = inject(WorkspaceService);
  private readonly route = inject(ActivatedRoute);

  protected readonly loading = signal(true);
  protected readonly project = signal<ProjectDto | null>(null);
  protected readonly errorKind = signal<ErrorKind | null>(null);
  protected readonly error = signal<AppError | null>(null);

  protected readonly slug = computed(() => this.project()?.slug ?? '');

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(async params => {
      const slug = params.get('slug');
      if (!slug) return;
      await this.load(slug);
    });
  }

  private async load(slug: string): Promise<void> {
    this.loading.set(true);
    this.errorKind.set(null);
    this.error.set(null);
    this.project.set(null);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.project.set(project);
    } catch (e: unknown) {
      this.classify(e);
    } finally {
      this.loading.set(false);
    }
  }

  private classify(e: unknown): void {
    if (e instanceof HttpErrorResponse) {
      if (e.status === 404) { this.errorKind.set('not-found'); return; }
      if (e.status === 403) { this.errorKind.set('forbidden'); return; }
      if (e.error && typeof e.error === 'object' && 'title' in e.error) {
        this.error.set(e.error as AppError);
      } else {
        this.error.set({ type: 'about:blank', title: 'Could not load project', status: e.status, detail: e.message });
      }
      this.errorKind.set('other');
      return;
    }
    this.error.set({
      type: 'about:blank',
      title: 'Could not load project',
      status: 0,
      detail: e instanceof Error ? e.message : 'An unexpected error occurred.',
    });
    this.errorKind.set('other');
  }
}
