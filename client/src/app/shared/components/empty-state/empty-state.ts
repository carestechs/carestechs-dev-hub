import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'empty-state',
  standalone: true,
  templateUrl: './empty-state.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string | null>(null);
}
