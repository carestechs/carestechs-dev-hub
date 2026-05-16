import { ChangeDetectionStrategy, Component, EventEmitter, input, Output } from '@angular/core';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton } from '../app-button/app-button';
import { AppErrorBanner } from '../app-error-banner/app-error-banner';
import { AppModal } from '../app-modal/app-modal';

export type ConfirmVariant = 'danger' | 'primary';

/**
 * Opinionated wrapper over AppModal for destructive (or otherwise consequential)
 * confirmations. Renders <c>message</c> as the body, plus a typed error banner if
 * the parent surfaces one. Emits <c>confirmed</c> on the primary button and
 * <c>cancelled</c> on the secondary button / overlay click / Escape.
 */
@Component({
  selector: 'confirm-dialog',
  standalone: true,
  imports: [AppModal, AppButton, AppErrorBanner],
  templateUrl: './confirm-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialog {
  readonly open = input<boolean>(false);
  readonly title = input<string>('Confirm');
  readonly message = input<string>('Are you sure?');
  readonly confirmLabel = input<string>('Confirm');
  readonly cancelLabel = input<string>('Cancel');
  readonly variant = input<ConfirmVariant>('danger');
  readonly working = input<boolean>(false);
  readonly error = input<AppError | null>(null);

  @Output() readonly confirmed = new EventEmitter<void>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected onConfirm(): void {
    if (this.working()) return;
    this.confirmed.emit();
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}
