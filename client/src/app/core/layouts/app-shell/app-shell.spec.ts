import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { AppShell } from './app-shell';
import { AppHeader } from './header';

describe('AppShell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  function render(setInputs: (f: ReturnType<typeof TestBed.createComponent<AppShell>>) => void = () => {}) {
    const fixture = TestBed.createComponent(AppShell);
    setInputs(fixture);
    fixture.detectChanges();
    return fixture;
  }

  it('renders header, sidebar, and router-outlet', () => {
    const fixture = render(f => f.componentRef.setInput('memberName', 'Operator'));
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

  it('emits logout when the header emits logout', () => {
    const fixture = render();
    let count = 0;
    fixture.componentInstance.logout.subscribe(() => count++);
    const header = fixture.debugElement.query(By.directive(AppHeader)).componentInstance as AppHeader;
    header.logout.emit();
    expect(count).toBe(1);
  });
});
