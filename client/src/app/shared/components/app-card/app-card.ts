import {
  ChangeDetectionStrategy,
  Component,
  computed,
  EventEmitter,
  HostBinding,
  HostListener,
  input,
  Output,
} from '@angular/core';

/**
 * Elevated content container per Modern Minimal: bg-white, rounded-xl, shadow-sm, p-6, no border.
 * When `clickable` is true, the card gets a hover lift, a focus ring, and emits `clicked`
 * on click / Enter / Space.
 */
@Component({
  selector: 'app-card',
  standalone: true,
  templateUrl: './app-card.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppCard {
  readonly clickable = input<boolean>(false);

  @Output() readonly clicked = new EventEmitter<void>();

  protected readonly classes = computed(() => {
    const base = 'block bg-white rounded-xl shadow-sm p-6';
    if (!this.clickable()) return base;
    return base +
      ' cursor-pointer hover:shadow-md hover:-translate-y-0.5 transition-all duration-200' +
      ' focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-300';
  });

  @HostBinding('attr.tabindex')
  protected get tabindex(): string | null {
    return this.clickable() ? '0' : null;
  }

  @HostBinding('attr.role')
  protected get role(): string | null {
    return this.clickable() ? 'button' : null;
  }

  @HostBinding('class')
  protected get hostClass(): string {
    return this.classes();
  }

  @HostListener('click')
  protected onClick(): void {
    if (this.clickable()) this.clicked.emit();
  }

  @HostListener('keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (!this.clickable()) return;
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.clicked.emit();
    }
  }
}
