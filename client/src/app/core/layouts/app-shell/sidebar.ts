import { ChangeDetectionStrategy, Component, EventEmitter, input, Output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

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

  protected onLinkClick(): void {
    // Parents (AppShell) listen so the mobile drawer can close on navigation.
    this.navigated.emit();
  }
}
