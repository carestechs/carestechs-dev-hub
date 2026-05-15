import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import type { AppError } from '../../core/errors/app-error';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    auth = TestBed.inject(AuthService);
  });

  function render() {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    return fixture;
  }

  function input(fixture: ReturnType<typeof render>, name: string): HTMLInputElement {
    return fixture.nativeElement.querySelector(`input[formcontrolname="${name}"]`) as HTMLInputElement;
  }

  function type(el: HTMLInputElement, value: string): void {
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  it('renders email + password fields and a submit button', () => {
    const fixture = render();
    expect(input(fixture, 'email')).toBeTruthy();
    expect(input(fixture, 'password')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[type=submit]')).toBeTruthy();
  });

  it('field-level validation: shows required errors after touch', () => {
    const fixture = render();
    // Trigger touched/invalid state by submitting the empty form.
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Email is required.');
    expect(fixture.nativeElement.textContent).toContain('Password is required.');
  });

  it('happy path: calls auth.login and routes to /', async () => {
    const fixture = render();
    const loginSpy = spyOn(auth, 'login').and.resolveTo();
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigateByUrl').and.resolveTo(true);

    type(input(fixture, 'email'), 'op@devhub.local');
    type(input(fixture, 'password'), 'ChangeMe123!');
    fixture.detectChanges();

    fixture.componentInstance['submit']();
    await Promise.resolve(); await Promise.resolve();

    expect(loginSpy).toHaveBeenCalledWith('op@devhub.local', 'ChangeMe123!');
    expect(navSpy).toHaveBeenCalledWith('/');
  });

  it('renders the server error from the auth response', async () => {
    const fixture = render();
    const error: AppError = {
      type: '/probs/unauthorized',
      title: 'Unauthorized',
      status: 401,
      detail: 'Invalid email or password.',
      correlationId: '00-abc',
    };
    spyOn(auth, 'login').and.rejectWith(new HttpErrorResponse({
      error,
      status: 401,
      statusText: 'Unauthorized',
    }));

    type(input(fixture, 'email'), 'op@devhub.local');
    type(input(fixture, 'password'), 'WRONG');
    fixture.detectChanges();
    fixture.componentInstance['submit']();
    await Promise.resolve(); await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    const banner = html.querySelector('[role=alert]');
    expect(banner?.textContent).toContain('Unauthorized');
    expect(banner?.textContent).toContain('Invalid email or password.');
  });
});
