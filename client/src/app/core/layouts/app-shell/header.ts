import { ChangeDetectionStrategy, Component, computed, EventEmitter, input, Output } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './header.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppHeader {
  readonly pendingCount = input<number>(0);
  readonly memberName = input<string>('');

  @Output() readonly menuToggle = new EventEmitter<void>();
  @Output() readonly logout = new EventEmitter<void>();

  protected readonly initials = computed(() => {
    const name = this.memberName().trim();
    if (!name) return '?';
    return name
      .split(/\s+/)
      .filter(Boolean)
      .map(part => part[0]!.toUpperCase())
      .slice(0, 2)
      .join('');
  });
}
