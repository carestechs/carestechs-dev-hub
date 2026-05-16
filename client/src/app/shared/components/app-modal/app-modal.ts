import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  EventEmitter,
  HostListener,
  inject,
  input,
  Output,
  signal,
  viewChild,
} from '@angular/core';

export type ModalWidth = 'sm' | 'md' | 'lg';

const FOCUSABLE_SELECTOR =
  'a[href], area[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
  'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Focus-trapping modal. Renders nothing while <c>open</c> is false. Closes on
 * Escape and (when <c>dismissOnOverlayClick</c>) on overlay click. Restores
 * focus to the previously active element on close.
 */
@Component({
  selector: 'app-modal',
  standalone: true,
  templateUrl: './app-modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppModal {
  readonly open = input<boolean>(false);
  readonly title = input<string>('');
  readonly width = input<ModalWidth>('md');
  readonly dismissOnOverlayClick = input<boolean>(true);

  @Output() readonly closed = new EventEmitter<void>();

  protected readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly previouslyFocused = signal<HTMLElement | null>(null);

  protected readonly widthClass = computed(() => {
    switch (this.width()) {
      case 'sm': return 'max-w-md';
      case 'lg': return 'max-w-2xl';
      default:   return 'max-w-lg';
    }
  });

  constructor() {
    effect(() => {
      const isOpen = this.open();
      if (typeof document === 'undefined') return;
      if (isOpen) {
        this.previouslyFocused.set(document.activeElement as HTMLElement | null);
        // Defer to allow the panel to render before focusing.
        queueMicrotask(() => this.focusFirst());
      } else {
        this.previouslyFocused()?.focus?.();
      }
    });
  }

  protected requestClose(): void {
    this.closed.emit();
  }

  protected onOverlayClick(): void {
    if (this.dismissOnOverlayClick()) this.requestClose();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.open()) this.requestClose();
  }

  @HostListener('document:keydown', ['$event'])
  protected onDocumentKeydown(event: KeyboardEvent): void {
    if (!this.open() || event.key !== 'Tab') return;
    const panel = this.panel()?.nativeElement;
    if (!panel) return;
    const focusable = Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
      .filter(el => !el.hasAttribute('aria-hidden'));
    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private focusFirst(): void {
    const panel = this.panel()?.nativeElement;
    if (!panel) return;
    const first = panel.querySelector<HTMLElement>(FOCUSABLE_SELECTOR);
    first?.focus();
  }
}
