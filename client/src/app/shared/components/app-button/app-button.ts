import { ChangeDetectionStrategy, Component, computed, EventEmitter, HostBinding, input, Output } from '@angular/core';
import { AppSpinner } from '../app-spinner/app-spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';
export type ButtonType = 'button' | 'submit' | 'reset';

@Component({
  selector: 'app-button',
  standalone: true,
  imports: [AppSpinner],
  templateUrl: './app-button.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppButton {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<ButtonType>('button');
  readonly disabled = input<boolean>(false);
  readonly loading = input<boolean>(false);
  readonly fullWidth = input<boolean>(false);
  readonly ariaLabel = input<string | null>(null);

  @Output() readonly clicked = new EventEmitter<MouseEvent>();

  protected readonly classes = computed(() => {
    const base =
      'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition ' +
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-300 ' +
      'focus-visible:ring-offset-2 focus-visible:ring-offset-white ' +
      'disabled:opacity-50 disabled:cursor-not-allowed';

    const variant: Record<ButtonVariant, string> = {
      primary:   'bg-sky-500 hover:bg-sky-600 active:bg-sky-700 text-white',
      secondary: 'bg-white border border-slate-300 hover:bg-slate-50 active:bg-slate-100 text-slate-700',
      ghost:     'text-sky-600 hover:bg-sky-50 active:bg-sky-100',
      danger:    'bg-red-500 hover:bg-red-600 active:bg-red-700 text-white',
    };

    const size: Record<ButtonSize, string> = {
      sm: 'text-sm h-9 px-3',
      md: 'text-base h-10 px-4',
      lg: 'text-lg h-12 px-5',
    };

    const width = this.fullWidth() ? 'w-full' : '';
    return `${base} ${variant[this.variant()]} ${size[this.size()]} ${width}`.trim();
  });

  protected readonly isInteractive = computed(() => !this.disabled() && !this.loading());

  @HostBinding('class.block')
  protected get hostBlock(): boolean { return this.fullWidth(); }

  @HostBinding('class.w-full')
  protected get hostFullWidth(): boolean { return this.fullWidth(); }

  protected onClick(event: MouseEvent): void {
    if (this.isInteractive()) {
      this.clicked.emit(event);
    } else {
      event.preventDefault();
      event.stopPropagation();
    }
  }
}
