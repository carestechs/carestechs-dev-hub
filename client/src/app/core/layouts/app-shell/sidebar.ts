import { ChangeDetectionStrategy, Component, computed, EventEmitter, inject, input, Output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import type { PendingActionDto } from '../../api/notifications.types';
import { PendingActionsStore } from '../../notifications/pending-actions.store';

const SIDEBAR_MAX = 5;

interface ProjectGroup { slug: string; actions: PendingActionDto[]; }

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppSidebar {
  readonly pendingCount = input<number>(0);
  readonly isOperator = input<boolean>(false);

  @Output() readonly navigated = new EventEmitter<void>();

  private readonly store = inject(PendingActionsStore);

  /** Top-N entries (most recent), grouped by projectSlug for the sidebar mini-list. */
  protected readonly groupedTopN = computed<ProjectGroup[]>(() => {
    const list = this.store.list().slice(0, SIDEBAR_MAX);
    const bySlug = new Map<string, PendingActionDto[]>();
    for (const a of list) {
      const arr = bySlug.get(a.projectSlug);
      if (arr) arr.push(a);
      else bySlug.set(a.projectSlug, [a]);
    }
    return Array.from(bySlug, ([slug, actions]) => ({ slug, actions }));
  });

  protected onLinkClick(): void {
    this.navigated.emit();
  }
}
