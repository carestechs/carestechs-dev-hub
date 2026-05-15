import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { HomePage } from './home.page';

describe('HomePage', () => {
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    auth = TestBed.inject(AuthService);
  });

  function setMember(name: string | null) {
    const slot = (auth as unknown as { _member: { set: (v: unknown) => void } })._member;
    slot.set(name === null ? null : { id: 'm1', displayName: name, email: 'op@devhub.local' });
  }

  it('greets the current member by display name', () => {
    setMember('Operator');
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1') as HTMLHeadingElement;
    expect(h1.textContent).toContain('Welcome back, Operator');
  });

  it('falls back to a generic welcome when no member is set', () => {
    setMember(null);
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1') as HTMLHeadingElement;
    expect(h1.textContent).toContain('Welcome');
    expect(h1.textContent).not.toContain('Welcome back,');
  });

  it('renders both empty-state placeholders with the documented copy', () => {
    setMember('Op');
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("You're all caught up.");
    expect(text).toContain('No projects yet.');
  });
});
