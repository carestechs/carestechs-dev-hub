import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuditService } from '../../core/api/audit.service';
import type { AuditEntryDto, AuditFilter } from '../../core/api/audit.types';
import { WorkspaceService } from '../../core/api/workspace.service';
import type { PageMeta, PageRequest, ProjectDto } from '../../core/api/workspace.types';
import { AuditTable } from './audit-table';

type ErrorKind = 'forbidden' | 'not-found' | 'other';

@Component({
  selector: 'project-audit-page',
  standalone: true,
  imports: [RouterLink, AuditTable],
  templateUrl: './project-audit.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectAuditPage {
  private readonly ws = inject(WorkspaceService);
  private readonly audit = inject(AuditService);
  private readonly route = inject(ActivatedRoute);

  protected readonly loading = signal(true);
  protected readonly project = signal<ProjectDto | null>(null);
  protected readonly rows = signal<AuditEntryDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);
  protected readonly filter = signal<AuditFilter>({});
  protected readonly page = signal<PageRequest>({ page: 1, pageSize: 50 });
  protected readonly errorKind = signal<ErrorKind | null>(null);

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
    this.project.set(null);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.project.set(project);
      await this.refresh();
    } catch (e: unknown) {
      this.classify(e);
    } finally {
      this.loading.set(false);
    }
  }

  private async refresh(): Promise<void> {
    const p = this.project();
    if (!p) return;
    try {
      const env = await this.audit.listProject(p.id, this.filter(), this.page());
      this.rows.set(env.data);
      this.meta.set(env.meta);
    } catch (e: unknown) {
      this.classify(e);
    }
  }

  protected async onFilterChanged(f: AuditFilter): Promise<void> {
    this.filter.set(f);
    this.page.update(p => ({ ...p, page: 1 }));
    await this.refresh();
  }

  protected async onPageChanged(p: { page: number; pageSize: number }): Promise<void> {
    this.page.update(prev => ({ ...prev, ...p }));
    await this.refresh();
  }

  private classify(e: unknown): void {
    if (e instanceof HttpErrorResponse) {
      if (e.status === 404) { this.errorKind.set('not-found'); return; }
      if (e.status === 403) { this.errorKind.set('forbidden'); return; }
      this.errorKind.set('other');
      return;
    }
    this.errorKind.set('other');
  }
}
