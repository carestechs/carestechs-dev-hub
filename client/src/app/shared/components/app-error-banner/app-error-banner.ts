import { ChangeDetectionStrategy, Component, EventEmitter, input, Output } from '@angular/core';
import { AppError } from '../../../core/errors/app-error';

@Component({
  selector: 'app-error-banner',
  standalone: true,
  templateUrl: './app-error-banner.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppErrorBanner {
  readonly error = input<AppError | null>(null);

  @Output() readonly retry = new EventEmitter<void>();

  protected copyCorrelationId(): void {
    const id = this.error()?.correlationId;
    if (id && typeof navigator !== 'undefined' && navigator.clipboard) {
      void navigator.clipboard.writeText(id);
    }
  }
}
