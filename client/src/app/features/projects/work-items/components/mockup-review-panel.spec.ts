import { ComponentRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MockupReviewPanel } from './mockup-review-panel';
import type { PanelSubmit } from './panel-submit';

const ARTEFACT = {
  currentTask: { id: 'T-001', title: 'Login screen', description: 'Build the login screen.' },
  mockupHtml: '<!DOCTYPE html><html><body><h1>Login</h1></body></html>',
  mockupDescription: 'Login screen — email + password + forgot link',
};

describe('MockupReviewPanel', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [MockupReviewPanel] }).compileComponents());

  function mount(overrides: { artefact?: unknown; canAct?: boolean; working?: boolean } = {}) {
    const fixture = TestBed.createComponent(MockupReviewPanel);
    const cref = fixture.componentRef as ComponentRef<MockupReviewPanel>;
    cref.setInput('artefact', overrides.artefact ?? ARTEFACT);
    cref.setInput('canAct', overrides.canAct ?? true);
    cref.setInput('working', overrides.working ?? false);
    fixture.detectChanges();
    return fixture;
  }

  it('renders mockupHtml in an iframe with a non-empty srcdoc', () => {
    const fixture = mount();
    const iframe = (fixture.nativeElement as HTMLElement).querySelector('iframe') as HTMLIFrameElement;
    expect(iframe).withContext('iframe must be present').toBeTruthy();
    // DomSanitizer wraps the value; the property is set (truthy) when HTML is provided.
    expect(iframe.srcdoc).toBeTruthy();
  });

  it('renders mockupDescription as caption', () => {
    const fixture = mount();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(ARTEFACT.mockupDescription);
  });

  it('approve emits { outcome: "approve", payload: {} }', () => {
    const fixture = mount();
    let emitted: PanelSubmit | null = null;
    fixture.componentInstance.submitted.subscribe(e => emitted = e);

    const cmp = fixture.componentInstance as unknown as { onApprove(): void };
    cmp.onApprove();

    expect(emitted as unknown as PanelSubmit).toEqual({ outcome: 'approve', payload: {} });
  });

  it('reject emits { outcome: "reject", payload: { feedback } }', () => {
    const fixture = mount();
    let emitted: PanelSubmit | null = null;
    fixture.componentInstance.submitted.subscribe(e => emitted = e);

    const cmp = fixture.componentInstance as unknown as {
      requesting: { set(v: boolean): void };
      feedback: { set(v: string): void };
      onRequestChanges(): void;
    };
    cmp.requesting.set(true);
    cmp.feedback.set('Needs a larger header.');
    fixture.detectChanges();
    cmp.onRequestChanges();

    expect(emitted as unknown as PanelSubmit).toEqual({ outcome: 'reject', payload: { feedback: 'Needs a larger header.' } });
  });

  it('reject does not emit when feedback is empty', () => {
    const fixture = mount();
    let count = 0;
    fixture.componentInstance.submitted.subscribe(() => count++);

    const cmp = fixture.componentInstance as unknown as {
      requesting: { set(v: boolean): void };
      feedback: { set(v: string): void };
      onRequestChanges(): void;
    };
    cmp.requesting.set(true);
    cmp.feedback.set('   ');
    fixture.detectChanges();
    cmp.onRequestChanges();

    expect(count).toBe(0);
  });

  it('approve does not emit when canAct is false', () => {
    const fixture = mount({ canAct: false });
    let count = 0;
    fixture.componentInstance.submitted.subscribe(() => count++);

    const cmp = fixture.componentInstance as unknown as { onApprove(): void };
    cmp.onApprove();

    expect(count).toBe(0);
  });

  it('shows fallback when mockupHtml is absent', () => {
    const fixture = mount({ artefact: { currentTask: {}, mockupHtml: '', mockupDescription: '' } });
    const iframe = (fixture.nativeElement as HTMLElement).querySelector('iframe');
    expect(iframe).toBeNull();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No mockup available');
  });
});
