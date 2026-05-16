import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { WorkspaceService } from '../../core/api/workspace.service';
import type { ProjectDto } from '../../core/api/workspace.types';
import { AuthService } from '../../core/auth/auth.service';
import { AppCard, EmptyState } from '../../shared';
import { ProjectCard } from '../projects/project-card';
import { PendingActionList } from './pending-action-list';

@Component({
  selector: 'home-page',
  standalone: true,
  imports: [RouterLink, AppCard, EmptyState, ProjectCard, PendingActionList],
  templateUrl: './home.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  private readonly auth = inject(AuthService);
  private readonly ws = inject(WorkspaceService);
  private readonly router = inject(Router);

  protected readonly displayName = computed(() => this.auth.currentMember()?.displayName ?? '');
  protected readonly projects = signal<ProjectDto[]>([]);
  protected readonly projectsLoading = signal(true);

  constructor() {
    void this.loadTopProjects();
  }

  private async loadTopProjects(): Promise<void> {
    this.projectsLoading.set(true);
    try {
      const env = await this.ws.listProjects({ page: 1, pageSize: 3, sortBy: 'updatedAt', sortDir: 'desc' });
      this.projects.set(env.data);
    } catch {
      // The home page is not the right place to surface a project-load error.
      // Leaving projects empty lets the empty-state branch render.
      this.projects.set([]);
    } finally {
      this.projectsLoading.set(false);
    }
  }

  protected onProjectOpen(p: ProjectDto): void {
    void this.router.navigate(['/projects', p.slug]);
  }
}
