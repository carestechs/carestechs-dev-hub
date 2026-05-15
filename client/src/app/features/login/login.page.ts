import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import type { AppError } from '../../core/errors/app-error';
import { AppButton, AppCard, AppErrorBanner, AppFormField } from '../../shared';

@Component({
  selector: 'login-page',
  standalone: true,
  imports: [ReactiveFormsModule, AppCard, AppButton, AppFormField, AppErrorBanner],
  templateUrl: './login.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<AppError | null>(null);

  protected get emailControl() { return this.form.controls.email; }
  protected get passwordControl() { return this.form.controls.password; }

  protected emailError(): string | null {
    const c = this.emailControl;
    if (!c.touched || c.valid) return null;
    if (c.errors?.['required']) return 'Email is required.';
    if (c.errors?.['email']) return 'Enter a valid email.';
    return null;
  }

  protected passwordError(): string | null {
    const c = this.passwordControl;
    if (!c.touched || c.valid) return null;
    if (c.errors?.['required']) return 'Password is required.';
    return null;
  }

  protected async submit(): Promise<void> {
    if (this.submitting()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.serverError.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      await this.router.navigateByUrl('/');
    } catch (err: unknown) {
      this.serverError.set(this.toAppError(err));
    } finally {
      this.submitting.set(false);
    }
  }

  private toAppError(err: unknown): AppError {
    if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object' && 'title' in err.error) {
      return err.error as AppError;
    }
    return {
      type: 'about:blank',
      title: 'Sign-in failed',
      status: 0,
      detail: err instanceof Error ? err.message : 'An unexpected error occurred.',
    };
  }
}
