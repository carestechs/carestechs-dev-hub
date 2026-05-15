import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AppCard, EmptyState } from '../../shared';

@Component({
  selector: 'home-page',
  standalone: true,
  imports: [AppCard, EmptyState],
  templateUrl: './home.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  private readonly auth = inject(AuthService);

  protected readonly displayName = computed(() => this.auth.currentMember()?.displayName ?? '');
}
