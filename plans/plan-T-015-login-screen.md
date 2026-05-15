# Implementation Plan: T-015 — Login screen

## Task Reference
- **Task ID:** T-015
- **Type:** Frontend
- **Workflow:** mockup-first
- **Complexity:** M
- **Rationale:** The single entry point for end users. Direct mapping to FEAT-001 AC-3.

## Overview
The Login page submits credentials via `AuthService.login()` and, on success, navigates to `/`. On failure, an `AppErrorBanner` renders the RFC 7807 detail. Mockup first.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/login.html`
**Action:** Create
Single-screen Tailwind mockup matching `docs/ui-specification.md` § Login (centered card on `bg-slate-50`, max-w-md, Poppins title, email + password fields, sign-in button, error banner state). Get approval before code.

### Step 1: LoginPage component
**Files:** `client/src/app/features/login/login.page.{ts,html,spec.ts}`
**Action:** Create
Standalone. Imports `ReactiveFormsModule`, `AppButton`, `AppFormField`, `AppErrorBanner`.
```ts
@Component({ ... })
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  readonly submitting = signal(false);
  readonly error = signal<AppError | null>(null);

  async submit() {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.submitting.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.form.controls.email.value, this.form.controls.password.value);
      await this.router.navigateByUrl('/');
    } catch (e: any) {
      this.error.set(e?.error ?? { type: 'about:blank', title: 'Sign-in failed', status: 0, detail: e?.message });
    } finally {
      this.submitting.set(false);
    }
  }
}
```
Template:
```html
<app-card>
  <h1 class="text-2xl font-heading font-semibold mb-4">Sign in</h1>
  <app-error-banner *ngIf="error()" [error]="error()" class="mb-4" />
  <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
    <app-form-field label="Email" [required]="true"
                    [error]="form.controls.email.touched && form.controls.email.invalid ? 'Enter a valid email.' : null">
      <input #input type="email" formControlName="email"
             class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10"
             autocomplete="email" />
    </app-form-field>
    <app-form-field label="Password" [required]="true"
                    [error]="form.controls.password.touched && form.controls.password.invalid ? 'Password is required.' : null">
      <input type="password" formControlName="password"
             class="block w-full border border-slate-300 focus:border-sky-500 focus:ring-2 focus:ring-sky-100 rounded-lg px-3 h-10"
             autocomplete="current-password" />
    </app-form-field>
    <app-button variant="primary" type="submit" [loading]="submitting()" [disabled]="submitting()" class="w-full">
      Sign in →
    </app-button>
  </form>
</app-card>
```

### Step 2: Specs
**File:** `client/src/app/features/login/login.page.spec.ts`
**Action:** Create
- Happy path: fill form, submit, `auth.login` called once, router navigated to `/`.
- Validation: empty fields → submit no-op; error texts render after touch.
- Server error: `auth.login` rejects with an `AppError` → banner renders title + detail; button re-enables.
- Loading state: while `auth.login` is pending, button shows spinner and is disabled.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `mockups/login.html` | Create | Stakeholder-approved mockup |
| `client/src/app/features/login/login.page.{ts,html,spec.ts}` | Create | Login screen |

## Edge Cases & Risks
- **Enter-key submission** — handled by `(ngSubmit)` on `<form>`. Confirm in the spec.
- **Email autocomplete + password manager** — `autocomplete="email"` / `autocomplete="current-password"` are needed for browser password managers to fill correctly.
- **Server returns `errors` map** (field-level validation) — render those into the banner detail; per-field rendering is a v1.1 polish.
- **Network failure (no response)** — falls through to the synthetic `AppError` from `problemDetailsInterceptor`; banner still renders.

## Acceptance Verification
- [ ] Mockup approved before implementation.
- [ ] Submitting valid seed credentials lands on `/` and renders the operator's name on Home.
- [ ] Invalid credentials produce an inline `app-error-banner` with the RFC 7807 `title` + `detail`; the button re-enables.
- [ ] Empty / invalid fields show field-level error text after blur.
- [ ] Loading state disables fields and the button; button shows inline spinner.
- [ ] Pressing Enter inside either field submits the form.
- [ ] All four spec scenarios pass under `ng test`.
