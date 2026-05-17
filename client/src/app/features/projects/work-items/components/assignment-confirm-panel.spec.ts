import { ComponentRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AssignmentConfirmPanel, type AssignmentConfirmSubmit } from './assignment-confirm-panel';

describe('AssignmentConfirmPanel', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [AssignmentConfirmPanel] }).compileComponents());

  function mount(
    overrides: { memberships?: unknown[]; currentTaskId?: string | null; canAct?: boolean; working?: boolean } = {},
  ) {
    const fixture = TestBed.createComponent(AssignmentConfirmPanel);
    const cref = fixture.componentRef as ComponentRef<AssignmentConfirmPanel>;
    cref.setInput('memberships', overrides.memberships ?? []);
    cref.setInput('currentTaskId', overrides.currentTaskId ?? null);
    cref.setInput('canAct', overrides.canAct ?? true);
    cref.setInput('working', overrides.working ?? false);
    fixture.detectChanges();
    return fixture;
  }

  it('picker mode submit emits the selected member display name as assignee', async () => {
    const fixture = mount({
      memberships: [
        { id: 'pm1', member: { id: 'm-alice', displayName: 'Alice', email: 'a@x' }, roles: ['reviewer'], createdAt: '2026-05-01T00:00:00Z' },
      ],
      currentTaskId: 'T-001',
    });
    const cmp = fixture.componentInstance as unknown as {
      selectedMemberId: { set(v: string): void };
      onSubmit(): void;
    };
    cmp.selectedMemberId.set('m-alice');
    fixture.detectChanges();

    let emitted: AssignmentConfirmSubmit | null = null;
    fixture.componentInstance.submitted.subscribe(e => emitted = e);
    cmp.onSubmit();

    expect(emitted as unknown as AssignmentConfirmSubmit).toEqual({
      outcome: 'confirmed',
      payload: { assignee: 'Alice' },
      taskId: 'T-001',
    });
  });

  it('free-text mode submit emits the typed string verbatim', async () => {
    const fixture = mount({ currentTaskId: 'T-002' });
    const cmp = fixture.componentInstance as unknown as {
      mode: { set(v: 'picker' | 'freetext'): void };
      freeTextValue: { set(v: string): void };
      onSubmit(): void;
    };
    cmp.mode.set('freetext');
    cmp.freeTextValue.set('external-vendor');
    fixture.detectChanges();

    let emitted: AssignmentConfirmSubmit | null = null;
    fixture.componentInstance.submitted.subscribe(e => emitted = e);
    cmp.onSubmit();

    expect(emitted as unknown as AssignmentConfirmSubmit).toEqual({
      outcome: 'confirmed',
      payload: { assignee: 'external-vendor' },
      taskId: 'T-002',
    });
  });

  it('empty value blocks submit (picker with nothing selected, free text empty)', () => {
    const fixture = mount({ memberships: [] });
    const cmp = fixture.componentInstance as unknown as {
      mode: { set(v: 'picker' | 'freetext'): void };
      freeTextValue: { set(v: string): void };
      onSubmit(): void;
    };

    // Picker mode, nothing selected
    let count = 0;
    fixture.componentInstance.submitted.subscribe(() => count++);
    cmp.onSubmit();
    expect(count).toBe(0);

    // Free-text mode with empty / whitespace value
    cmp.mode.set('freetext');
    cmp.freeTextValue.set('   ');
    fixture.detectChanges();
    cmp.onSubmit();
    expect(count).toBe(0);
  });

  it('confirm button is disabled when canAct=false', () => {
    const fixture = mount({
      canAct: false,
      memberships: [
        { id: 'pm1', member: { id: 'm-alice', displayName: 'Alice', email: 'a@x' }, roles: ['reviewer'], createdAt: '2026-05-01T00:00:00Z' },
      ],
    });
    const cmp = fixture.componentInstance as unknown as {
      selectedMemberId: { set(v: string): void };
    };
    cmp.selectedMemberId.set('m-alice');
    fixture.detectChanges();

    const btn = (fixture.nativeElement as HTMLElement).querySelector('button[type="button"]') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain("You don't have the role required for this checkpoint.");
  });
});
