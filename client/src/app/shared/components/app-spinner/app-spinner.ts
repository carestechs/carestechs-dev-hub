import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type SpinnerSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-spinner',
  standalone: true,
  templateUrl: './app-spinner.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppSpinner {
  readonly size = input<SpinnerSize>('md');
  readonly ariaLabel = input<string>('Loading');

  protected readonly sizeClass = computed(() => {
    switch (this.size()) {
      case 'sm': return 'h-4 w-4';
      case 'lg': return 'h-6 w-6';
      default:   return 'h-5 w-5';
    }
  });
}
