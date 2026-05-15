import {
  AfterContentInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  contentChild,
  effect,
  ElementRef,
  input,
} from '@angular/core';

/**
 * Label + projected control + helper/error slot. The consumer projects an `<input>`,
 * `<textarea>`, or `<select>`; AppFormField finds it via contentChild and toggles
 * `aria-invalid` based on the `error` input.
 */
@Component({
  selector: 'app-form-field',
  standalone: true,
  templateUrl: './app-form-field.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppFormField implements AfterContentInit {
  readonly label = input<string>('');
  readonly helperText = input<string | null>(null);
  readonly error = input<string | null>(null);
  readonly required = input<boolean>(false);

  protected readonly hasError = computed(() => !!this.error());

  // Find the first focusable form control among projected content.
  private readonly projectedControl = contentChild<ElementRef<HTMLElement>>('input', { descendants: true });

  constructor() {
    effect(() => {
      const el = this.projectedControl()?.nativeElement;
      if (!el) return;
      if (this.hasError()) {
        el.setAttribute('aria-invalid', 'true');
      } else {
        el.removeAttribute('aria-invalid');
      }
    });
  }

  ngAfterContentInit(): void {
    // Initial sync — effect handles updates.
  }
}
