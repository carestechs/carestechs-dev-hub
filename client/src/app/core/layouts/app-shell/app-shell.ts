import {
  ChangeDetectionStrategy,
  Component,
  computed,
  HostListener,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../../auth/auth.service';
import { AppHeader } from './header';
import { AppSidebar } from './sidebar';

/**
 * Authenticated app shell — persistent header + collapsible sidebar + content outlet.
 * Reads AuthService signals directly so child routes don't have to plumb member state.
 * pendingCount is 0 in v1 (FEAT-005 will wire the live notification stream).
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, AppHeader, AppSidebar],
  templateUrl: './app-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly memberName = computed(() => this.auth.currentMember()?.displayName ?? '');
  protected readonly isOperator = this.auth.isOperator;
  protected readonly pendingCount = signal(0); // FEAT-005 wires this to the live stream

  protected readonly drawerOpen = signal(false);
  protected readonly drawerVisible = computed(() => this.drawerOpen());

  protected toggleDrawer(): void { this.drawerOpen.update(o => !o); }
  protected closeDrawer(): void { this.drawerOpen.set(false); }

  protected async onLogout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
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
