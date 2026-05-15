import { TestBed } from '@angular/core/testing';
import type { AppError } from '../../../core/errors/app-error';
import { AppErrorBanner } from './app-error-banner';

describe('AppErrorBanner', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AppErrorBanner] }).compileComponents();
  });

  function render(error: AppError | null) {
    const fixture = TestBed.createComponent(AppErrorBanner);
    fixture.componentRef.setInput('error', error);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders nothing when error is null', () => {
    expect(render(null).querySelector('[role=alert]')).toBeNull();
  });

  it('renders title, detail, and correlationId', () => {
    const html = render({
      type: '/probs/unauthorized',
      title: 'Unauthorized',
      status: 401,
      detail: 'Invalid email or password.',
      correlationId: '00-abc-123',
    });
    const alert = html.querySelector('[role=alert]')!;
    expect(alert.textContent).toContain('Unauthorized');
    expect(alert.textContent).toContain('Invalid email or password.');
    expect(alert.textContent).toContain('00-abc-123');
  });

  it('omits the Copy button when no correlationId', () => {
    const html = render({ type: 'about:blank', title: 'Network error', status: 0 });
    expect(html.querySelector('button')).toBeNull();
  });
});
