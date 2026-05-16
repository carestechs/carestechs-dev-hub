import { ChangeDetectionStrategy, Component, EventEmitter, input, Output } from '@angular/core';

export interface TimelineStep {
  key: string;
  label: string;
  state: 'approved' | 'active' | 'pending' | 'rejected';
  signal?: { signaledBy: string; signaledAt: string; outcome: string } | null;
}

@Component({
  selector: 'lifecycle-timeline',
  standalone: true,
  templateUrl: './lifecycle-timeline.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LifecycleTimeline {
  readonly steps = input<TimelineStep[]>([]);

  @Output() readonly stepSelected = new EventEmitter<TimelineStep>();

  protected badgeClass(state: TimelineStep['state']): string {
    switch (state) {
      case 'approved': return 'bg-emerald-500 text-white';
      case 'rejected': return 'bg-red-500 text-white';
      case 'active':   return 'bg-sky-500 text-white';
      default:         return 'bg-slate-200 text-slate-500';
    }
  }

  protected badgeIcon(state: TimelineStep['state']): string {
    switch (state) {
      case 'approved': return '✓';
      case 'rejected': return '✗';
      case 'active':   return '●';
      default:         return '○';
    }
  }

  protected onClick(step: TimelineStep): void {
    this.stepSelected.emit(step);
  }
}
