import { TestBed } from '@angular/core/testing';
import { AppCard } from './app-card';

describe('AppCard', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AppCard] }).compileComponents();
  });

  it('renders the default elevated card without role/tabindex', () => {
    const fixture = TestBed.createComponent(AppCard);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.className).toContain('bg-white');
    expect(host.className).toContain('shadow-sm');
    expect(host.getAttribute('tabindex')).toBeNull();
    expect(host.getAttribute('role')).toBeNull();
  });

  it('applies clickable affordances when clickable=true', () => {
    const fixture = TestBed.createComponent(AppCard);
    fixture.componentRef.setInput('clickable', true);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.className).toContain('hover:shadow-md');
    expect(host.className).toContain('focus-visible:ring-sky-300');
    expect(host.getAttribute('tabindex')).toBe('0');
    expect(host.getAttribute('role')).toBe('button');
  });

  it('emits clicked on click + Enter + Space when clickable', () => {
    const fixture = TestBed.createComponent(AppCard);
    fixture.componentRef.setInput('clickable', true);
    fixture.detectChanges();

    let count = 0;
    fixture.componentInstance.clicked.subscribe(() => count++);
    const host = fixture.nativeElement as HTMLElement;
    host.click();
    host.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    host.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));
    expect(count).toBe(3);
  });

  it('does not emit clicked when not clickable', () => {
    const fixture = TestBed.createComponent(AppCard);
    fixture.detectChanges();
    let count = 0;
    fixture.componentInstance.clicked.subscribe(() => count++);
    (fixture.nativeElement as HTMLElement).click();
    (fixture.nativeElement as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(count).toBe(0);
  });
});
