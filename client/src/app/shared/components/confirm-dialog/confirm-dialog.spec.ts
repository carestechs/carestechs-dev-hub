import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConfirmDialog } from './confirm-dialog';

@Component({
  standalone: true,
  imports: [ConfirmDialog],
  template: `
    <confirm-dialog
      [open]="open()"
      [working]="working"
      title="Delete?"
      message="Are you sure?"
      confirmLabel="Delete"
      (confirmed)="confirmedCount = confirmedCount + 1"
      (cancelled)="cancelledCount = cancelledCount + 1"
    />
  `,
})
class Host {
  open = signal(true);
  working = false;
  confirmedCount = 0;
  cancelledCount = 0;
}

describe('ConfirmDialog', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
  });

  function render(setup: (host: Host) => void = () => {}) {
    const fixture = TestBed.createComponent(Host);
    setup(fixture.componentInstance);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the title and message inside the modal panel', () => {
    const fixture = render();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Delete?');
    expect(html.textContent).toContain('Are you sure?');
  });

  it('emits confirmed on the primary (Delete) button click', () => {
    const fixture = render();
    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    const deleteBtn = buttons.find(b => b.textContent?.includes('Delete') && !b.getAttribute('aria-label')) as HTMLButtonElement;
    deleteBtn.click();
    expect(fixture.componentInstance.confirmedCount).toBe(1);
  });

  it('emits cancelled on the secondary (Cancel) button click', () => {
    const fixture = render();
    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    const cancelBtn = buttons.find(b => b.textContent?.includes('Cancel')) as HTMLButtonElement;
    cancelBtn.click();
    expect(fixture.componentInstance.cancelledCount).toBe(1);
  });

  it('disables both buttons when working=true', () => {
    const fixture = render(h => { h.working = true; });
    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .filter(b => b.textContent?.match(/Delete|Cancel/));
    expect(buttons.every(b => (b as HTMLButtonElement).disabled)).toBeTrue();
  });
});
