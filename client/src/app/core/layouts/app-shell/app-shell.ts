import {
  ChangeDetectionStrategy,
  Component,
  computed,
  HostListener,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../../auth/auth.service';
import { PendingActionsStore } from '../../notifications/pending-actions.store';
import { AppHeader } from './header';
import { AppSidebar } from './sidebar';

/**
 * Authenticated app shell — persistent header + collapsible sidebar + content outlet.
 * Boots the PendingActionsStore on mount so the live "Pending on you" surfaces are
 * available everywhere in the authed app; shuts it down on logout / destroy.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, AppHeader, AppSidebar],
  templateUrl: './app-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly pending = inject(PendingActionsStore);

  protected readonly memberName = computed(() => this.auth.currentMember()?.displayName ?? '');
  protected readonly isOperator = this.auth.isOperator;
  protected readonly pendingCount = this.pending.count;
  protected readonly pendingBadge = this.pending.badgeText;

  protected readonly drawerOpen = signal(false);
  protected readonly drawerVisible = computed(() => this.drawerOpen());

  ngOnInit(): void {
    void this.pending.start();
  }

  ngOnDestroy(): void {
    this.pending.shutdown();
  }

  protected toggleDrawer(): void { this.drawerOpen.update(o => !o); }
  protected closeDrawer(): void { this.drawerOpen.set(false); }

  protected async onLogout(): Promise<void> {
    this.pending.shutdown();
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
