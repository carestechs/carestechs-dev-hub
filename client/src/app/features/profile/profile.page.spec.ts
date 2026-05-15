import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ProfilePage } from './profile.page';

describe('ProfilePage', () => {
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProfilePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    auth = TestBed.inject(AuthService);
    (auth as unknown as { _member: { set: (v: unknown) => void } })._member.set({
      id: 'm1', displayName: 'Operator', email: 'op@devhub.local',
    });
  });

  it('renders member details', () => {
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Operator');
    expect(text).toContain('op@devhub.local');
    expect(text).toContain('No project memberships yet.');
  });

  it('Sign out calls auth.logout then routes to /login', async () => {
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();

    const logoutSpy = spyOn(auth, 'logout').and.resolveTo();
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigateByUrl').and.resolveTo(true);

    const button = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;
    button.click();
    await Promise.resolve(); await Promise.resolve();

    expect(logoutSpy).toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith('/login');
  });
});
