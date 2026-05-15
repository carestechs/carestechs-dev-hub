import {
  ChangeDetectionStrategy,
  Component,
  computed,
  EventEmitter,
  HostListener,
  input,
  Output,
  signal,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppHeader } from './header';
import { AppSidebar } from './sidebar';

/**
 * Authenticated app shell: persistent header + collapsible sidebar + content outlet.
 * Inputs are kept dumb so a parent component (App) can wire AuthService signals to them
 * once T-013 lands. AppShell itself has no AuthService dependency.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, AppHeader, AppSidebar],
  templateUrl: './app-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  readonly memberName = input<string>('');
  readonly pendingCount = input<number>(0);
  readonly isOperator = input<boolean>(false);

  @Output() readonly logout = new EventEmitter<void>();

  protected readonly drawerOpen = signal(false);
  protected readonly drawerVisible = computed(() => this.drawerOpen());

  protected toggleDrawer(): void {
    this.drawerOpen.update(o => !o);
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  /** Auto-close the drawer when the viewport crosses into md+. */
  @HostListener('window:resize')
  protected onResize(): void {
    if (typeof window === 'undefined') return;
    if (window.matchMedia('(min-width: 768px)').matches) {
      this.drawerOpen.set(false);
    }
  }
}
