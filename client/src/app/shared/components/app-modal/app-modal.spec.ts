import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppModal } from './app-modal';

@Component({
  standalone: true,
  imports: [AppModal],
  template: `
    <app-modal [open]="open()" [title]="title" [dismissOnOverlayClick]="dismissOnOverlay" (closed)="closeCount = closeCount + 1">
      <button #first type="button" data-test="first">First</button>
      <button #last type="button" data-test="last">Last</button>
      <button modal-footer type="button" data-test="footer">Footer</button>
    </app-modal>
  `,
})
class Host {
  open = signal(false);
  title = 'Test modal';
  dismissOnOverlay = true;
  closeCount = 0;
}

describe('AppModal', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
  });

  function render(setup: (host: Host) => void = () => {}) {
    const fixture = TestBed.createComponent(Host);
    setup(fixture.componentInstance);
    fixture.detectChanges();
    return fixture;
  }

  it('renders nothing when closed', () => {
    const fixture = render();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=dialog]')).toBeNull();
  });

  it('renders the panel when opened, with the title', () => {
    const fixture = render(h => h.open.set(true));
    const dlg = (fixture.nativeElement as HTMLElement).querySelector('[role=dialog]');
    expect(dlg).not.toBeNull();
    expect(dlg?.textContent).toContain('Test modal');
  });

  it('emits closed on Escape', () => {
    const fixture = render(h => h.open.set(true));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(fixture.componentInstance.closeCount).toBe(1);
  });

  it('emits closed on overlay click when dismissOnOverlayClick=true', () => {
    const fixture = render(h => h.open.set(true));
    const overlay = (fixture.nativeElement as HTMLElement).querySelector('.bg-slate-900\\/40') as HTMLElement;
    overlay.click();
    expect(fixture.componentInstance.closeCount).toBe(1);
  });

  it('does NOT emit closed on overlay click when dismissOnOverlayClick=false', () => {
    const fixture = render(h => { h.dismissOnOverlay = false; h.open.set(true); });
    const overlay = (fixture.nativeElement as HTMLElement).querySelector('.bg-slate-900\\/40') as HTMLElement;
    overlay.click();
    expect(fixture.componentInstance.closeCount).toBe(0);
  });

  it('emits closed on header close button', () => {
    const fixture = render(h => h.open.set(true));
    const closeBtn = (fixture.nativeElement as HTMLElement).querySelector('button[aria-label="Close"]') as HTMLButtonElement;
    closeBtn.click();
    expect(fixture.componentInstance.closeCount).toBe(1);
  });
});
