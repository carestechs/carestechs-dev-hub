import { ChangeDetectionStrategy, Component, EventEmitter, input, Output, signal } from '@angular/core';

export interface SubmittedOutcome {
  outcome: string;
  payload: { notes: string };
}

@Component({
  selector: 'checkpoint-action-bar',
  standalone: true,
  templateUrl: './checkpoint-action-bar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckpointActionBar {
  readonly allowedOutcomes = input<string[]>([]);
  readonly requiredRoleKey = input<string>('');
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);
  readonly submittingOutcome = input<string | null>(null);

  @Output() readonly submitted = new EventEmitter<SubmittedOutcome>();

  protected readonly notes = signal<string>('');

  protected onNotesInput(ev: Event): void {
    const target = ev.target as HTMLTextAreaElement | null;
    this.notes.set(target?.value ?? '');
  }

  protected onClick(outcome: string): void {
    if (!this.canAct() || this.working()) return;
    this.submitted.emit({ outcome, payload: { notes: this.notes() } });
  }

  protected variant(outcome: string): string {
    switch (outcome) {
      case 'approve': return 'bg-emerald-500 hover:bg-emerald-600 text-white';
      case 'reject':  return 'bg-red-500 hover:bg-red-600 text-white';
      case 'revise':  return 'bg-amber-500 hover:bg-amber-600 text-white';
      default:        return 'bg-sky-500 hover:bg-sky-600 text-white';
    }
  }

  protected icon(outcome: string): string {
    switch (outcome) {
      case 'approve': return '✓';
      case 'reject':  return '✗';
      case 'revise':  return '↺';
      default:        return '→';
    }
  }
}
