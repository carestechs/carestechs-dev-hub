import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuditService } from '../../core/api/audit.service';
import type { AuditEntryDto } from '../../core/api/audit.types';
import type { PendingActionDto } from '../../core/api/notifications.types';
import { WorkspaceService } from '../../core/api/workspace.service';
import type { ProjectDto } from '../../core/api/workspace.types';
import { PendingActionsStore } from '../../core/notifications/pending-actions.store';

interface ProjectGroup { slug: string; actions: PendingActionDto[]; }

@Component({
  selector: 'operator-dashboard-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './operator-dashboard.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperatorDashboardPage {
  private readonly ws = inject(WorkspaceService);
  private readonly audit = inject(AuditService);
  protected readonly pending = inject(PendingActionsStore);

  protected readonly loading = signal(true);
  protected readonly errored = signal(false);
  protected readonly projects = signal<ProjectDto[]>([]);
  protected readonly recentEvents = signal<AuditEntryDto[]>([]);

  /** Sum of `inFlightWorkItems` across all loaded projects. */
  protected readonly inFlightTotal = computed(() =>
    this.projects().reduce((acc, p) => acc + p.inFlightWorkItems, 0));

  /** Projects with at least one in-flight work item, descending by count. */
  protected readonly projectsWithInFlight = computed(() =>
    this.projects()
      .filter(p => p.inFlightWorkItems > 0)
      .sort((a, b) => b.inFlightWorkItems - a.inFlightWorkItems));

  /** Pending approvals grouped by projectSlug for the middle panel. */
  protected readonly pendingGroups = computed<ProjectGroup[]>(() => {
    const bySlug = new Map<string, PendingActionDto[]>();
    for (const a of this.pending.list()) {
      const arr = bySlug.get(a.projectSlug);
      if (arr) arr.push(a);
      else bySlug.set(a.projectSlug, [a]);
    }
    return Array.from(bySlug, ([slug, actions]) => ({ slug, actions }));
  });

  protected readonly pendingCount = this.pending.count;

  constructor() {
    void this.refresh();
  }

  protected async refresh(): Promise<void> {
    this.loading.set(true);
    this.errored.set(false);
    try {
      // Load projects + admin audit in parallel; refresh the pending store too.
      const [projects, audit] = await Promise.all([
        this.ws.listProjects({ page: 1, pageSize: 100, sortBy: 'updatedAt', sortDir: 'desc' }),
        this.audit.listAdmin({}, { page: 1, pageSize: 50 }),
        this.pending.refresh(),
      ] as const).then(results => [results[0], results[1]] as const);

      this.projects.set(projects.data);
      // v1 client-side filter to Denied + Failed (the API takes one outcome at a time;
      // a multi-value filter is a v2 backend gain). 50-row page typically contains a few.
      this.recentEvents.set(audit.data.filter(a => a.outcome === 'Denied' || a.outcome === 'Failed'));
    } catch (e: unknown) {
      this.errored.set(e instanceof HttpErrorResponse);
    } finally {
      this.loading.set(false);
    }
  }

  protected outcomePillClass(outcome: 'Denied' | 'Failed'): string {
    return outcome === 'Denied' ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-800';
  }
}
