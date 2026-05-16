import { ChangeDetectionStrategy, Component, EventEmitter, input, Output } from '@angular/core';
import type { CheckpointSignalDto } from '../../../../core/api/work-items.types';

@Component({
  selector: 'signal-history-list',
  standalone: true,
  templateUrl: './signal-history-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignalHistoryList {
  readonly signals = input<CheckpointSignalDto[]>([]);
  readonly loading = input<boolean>(false);
  readonly canLoadMore = input<boolean>(false);

  @Output() readonly loadMore = new EventEmitter<void>();

  protected outcomeClass(outcome: string): string {
    if (outcome === 'approve') return 'text-emerald-700';
    if (outcome === 'reject') return 'text-red-700';
    if (outcome === 'revise') return 'text-amber-700';
    return 'text-slate-600';
  }
}
