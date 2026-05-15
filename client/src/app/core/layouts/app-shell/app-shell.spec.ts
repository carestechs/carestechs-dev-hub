import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { AuthService } from '../../auth/auth.service';
import { AppShell } from './app-shell';
import { AppHeader } from './header';

describe('AppShell', () => {
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    auth = TestBed.inject(AuthService);
  });

  function render() {
    const fixture = TestBed.createComponent(AppShell);
    fixture.detectChanges();
    return fixture;
  }

  it('renders header, sidebar, router-outlet, and the current member name', () => {
    auth.setAccessToken('jwt');
    (auth as unknown as { _member: { set: (v: unknown) => void } })._member.set({
      id: 'm1', displayName: 'Operator', email: 'op@devhub.local',
    });
    const fixture = render();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('app-header')).toBeTruthy();
    expect(html.querySelector('app-sidebar')).toBeTruthy();
    expect(html.querySelector('router-outlet')).toBeTruthy();
    expect(html.textContent).toContain('Operator');
  });

  it('toggles the mobile drawer on menu-toggle from the header', () => {
    const fixture = render();
    const header = fixture.debugElement.query(By.directive(AppHeader)).componentInstance as AppHeader;
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('app-sidebar').length).toBe(1);
    header.menuToggle.emit();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('app-sidebar').length).toBe(2);
  });

  it('logout: calls AuthService.logout and routes to /login', async () => {
    const fixture = render();
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const logoutSpy = spyOn(auth, 'logout').and.resolveTo();
    const header = fixture.debugElement.query(By.directive(AppHeader)).componentInstance as AppHeader;

    header.logout.emit();
    await Promise.resolve(); await Promise.resolve();

    expect(logoutSpy).toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith('/login');
  });
});
