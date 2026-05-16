import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PendingActionsStore } from '../../core/notifications/pending-actions.store';

@Component({
  selector: 'pending-action-list',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './pending-action-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PendingActionList {
  protected readonly store = inject(PendingActionsStore);

  protected onReconnect(): void {
    void this.store.reconnect();
  }
}
