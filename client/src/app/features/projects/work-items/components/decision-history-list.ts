import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { CheckpointSignalDto } from '../../../../core/api/work-items.types';

@Component({
  selector: 'decision-history-list',
  standalone: true,
  template: `
    @if (signals().length === 0) {
      <p class="text-sm text-slate-500">No decisions yet.</p>
    } @else {
      <ul class="divide-y divide-slate-200 text-sm">
        @for (s of signals(); track s.id) {
          <li class="py-2">
            <span class="font-medium">{{ s.signaledBy.displayName }}</span> ·
            <span [class]="outcomeClass(s.outcome)">{{ s.outcome }}</span>
            on <span class="font-mono text-xs bg-slate-100 rounded px-1.5 py-0.5">{{ s.checkpointKey }}</span>
            <span class="text-slate-500 text-xs">· {{ s.signaledAt.slice(0, 10) }}</span>
            @if (notesFrom(s.payload); as notes) {
              <span class="text-slate-500 text-xs"> · "{{ notes }}"</span>
            }
          </li>
        }
      </ul>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DecisionHistoryList {
  readonly signals = input<CheckpointSignalDto[]>([]);

  protected outcomeClass(outcome: string): string {
    if (outcome === 'approve') return 'text-emerald-700';
    if (outcome === 'reject') return 'text-red-700';
    if (outcome === 'revise') return 'text-amber-700';
    return 'text-slate-600';
  }

  protected notesFrom(payload: unknown): string | null {
    if (payload && typeof payload === 'object' && 'notes' in (payload as Record<string, unknown>)) {
      const v = (payload as Record<string, unknown>)['notes'];
      if (typeof v === 'string' && v.length > 0) return v;
    }
    return null;
  }
}
