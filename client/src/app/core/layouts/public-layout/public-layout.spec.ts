import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { PublicLayout } from './public-layout';

describe('PublicLayout', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PublicLayout],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the DevHub wordmark and a router-outlet', () => {
    const fixture = TestBed.createComponent(PublicLayout);
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('DevHub');
    expect(html.querySelector('router-outlet')).toBeTruthy();
  });
});
