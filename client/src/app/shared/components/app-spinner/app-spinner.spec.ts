import { TestBed } from '@angular/core/testing';
import { AppSpinner } from './app-spinner';

describe('AppSpinner', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AppSpinner] }).compileComponents();
  });

  function render(size?: 'sm' | 'md' | 'lg') {
    const fixture = TestBed.createComponent(AppSpinner);
    if (size) fixture.componentRef.setInput('size', size);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('svg') as SVGElement;
  }

  it('defaults to md size', () => {
    expect(render().getAttribute('class')).toContain('h-5 w-5');
  });

  it('renders sm size', () => {
    expect(render('sm').getAttribute('class')).toContain('h-4 w-4');
  });

  it('renders lg size', () => {
    expect(render('lg').getAttribute('class')).toContain('h-6 w-6');
  });

  it('exposes an aria-label', () => {
    const fixture = TestBed.createComponent(AppSpinner);
    fixture.componentRef.setInput('ariaLabel', 'Signing in');
    fixture.detectChanges();
    const svg = fixture.nativeElement.querySelector('svg') as SVGElement;
    expect(svg.getAttribute('aria-label')).toBe('Signing in');
  });
});
