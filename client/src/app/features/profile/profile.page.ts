import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { AppButton, AppCard } from '../../shared';

@Component({
  selector: 'profile-page',
  standalone: true,
  imports: [AppCard, AppButton],
  templateUrl: './profile.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly member = this.auth.currentMember;
  protected readonly memberships = this.auth.memberships;
  protected readonly displayName = computed(() => this.auth.currentMember()?.displayName ?? '');

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
