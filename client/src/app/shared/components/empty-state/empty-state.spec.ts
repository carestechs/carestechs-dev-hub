import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';

@Component({
  standalone: true,
  imports: [EmptyState],
  template: `
    <empty-state [title]="title" [description]="description">
      <button>CTA</button>
    </empty-state>
  `,
})
class Host {
  title = 'Nothing to see';
  description: string | null = null;
}

describe('EmptyState', () => {
  it('renders the title and projected CTA', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('h3')!.textContent).toContain('Nothing to see');
    expect(html.querySelector('button')!.textContent).toContain('CTA');
  });

  it('renders the description when provided', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.description = 'You are caught up.';
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('p')!.textContent).toContain('You are caught up.');
  });
});
