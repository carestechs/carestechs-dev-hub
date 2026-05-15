import { TestBed } from '@angular/core/testing';
import { AppButton, type ButtonVariant } from './app-button';

describe('AppButton', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AppButton] }).compileComponents();
  });

  function render(setInputs: (fixture: ReturnType<typeof TestBed.createComponent<AppButton>>) => void = () => {}) {
    const fixture = TestBed.createComponent(AppButton);
    setInputs(fixture);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('button') as HTMLButtonElement;
  }

  it('renders a button with primary variant by default', () => {
    const btn = render();
    expect(btn.className).toContain('bg-sky-500');
    expect(btn.disabled).toBeFalse();
  });

  (['primary', 'secondary', 'ghost', 'danger'] as ButtonVariant[]).forEach(variant => {
    it(`renders ${variant} variant`, () => {
      const btn = render(f => f.componentRef.setInput('variant', variant));
      const expected: Record<ButtonVariant, string> = {
        primary: 'bg-sky-500',
        secondary: 'border-slate-300',
        ghost: 'text-sky-600',
        danger: 'bg-red-500',
      };
      expect(btn.className).toContain(expected[variant]);
    });
  });

  it('shows the spinner when loading and disables clicks', () => {
    const fixture = TestBed.createComponent(AppButton);
    let clicked = 0;
    fixture.componentInstance.clicked.subscribe(() => clicked++);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(btn.disabled).toBeTrue();
    expect(btn.getAttribute('aria-busy')).toBe('true');
    expect(fixture.nativeElement.querySelector('app-spinner')).toBeTruthy();

    btn.click();
    expect(clicked).toBe(0);
  });

  it('emits clicked when interactive', () => {
    const fixture = TestBed.createComponent(AppButton);
    let received: MouseEvent | undefined;
    fixture.componentInstance.clicked.subscribe(e => received = e);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    expect(received).toBeDefined();
  });

  it('does not emit clicked when disabled', () => {
    const fixture = TestBed.createComponent(AppButton);
    let clicked = 0;
    fixture.componentInstance.clicked.subscribe(() => clicked++);
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    expect(clicked).toBe(0);
  });
});
