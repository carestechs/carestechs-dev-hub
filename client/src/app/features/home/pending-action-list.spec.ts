import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { PendingActionDto } from '../../core/api/notifications.types';
import { PendingActionsStore } from '../../core/notifications/pending-actions.store';
import { PendingActionList } from './pending-action-list';

class StubStore {
  list = signal<PendingActionDto[]>([]);
  count = signal(0);
  connected = signal(true);
  loading = signal(false);
  badgeText = signal<string | null>(null);
  reconnect = jasmine.createSpy('reconnect');
}

describe('PendingActionList', () => {
  let stub: StubStore;

  beforeEach(async () => {
    stub = new StubStore();
    await TestBed.configureTestingModule({
      imports: [PendingActionList],
      providers: [
        provideRouter([]),
        { provide: PendingActionsStore, useValue: stub },
      ],
    }).compileComponents();
  });

  it('renders the empty state when the list is empty', () => {
    const fixture = TestBed.createComponent(PendingActionList);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("You're all caught up.");
  });

  it('renders rows with project slug + checkpoint + Review link when populated', () => {
    stub.list.set([
      {
        projectId: 'p1', projectSlug: 'alpha',
        workItemId: 'w1', workItemTitle: 'Demo task',
        checkpointKey: 'approve', checkpointDisplayName: 'Approve',
        raisedAt: '2026-05-01T10:00:00Z',
      },
    ]);
    stub.count.set(1);

    const fixture = TestBed.createComponent(PendingActionList);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo task');
    expect(text).toContain('alpha');
    expect(text).toContain('Approve');
    expect(text).toContain('Review →');
  });

  it('per-task row renders with — <taskId> suffix and queryParams routerLink (FEAT-009)', () => {
    stub.list.set([
      {
        projectId: 'p1', projectSlug: 'alpha',
        workItemId: 'w1', workItemTitle: 'Multi-task feature',
        checkpointKey: 'assignment-confirmed', checkpointDisplayName: 'Confirm task assignment',
        raisedAt: '2026-05-01T10:00:00Z',
        taskId: 'T-001',
      },
    ]);
    stub.count.set(1);

    const fixture = TestBed.createComponent(PendingActionList);
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Multi-task feature');
    expect(html.textContent).toContain('— ');
    expect(html.textContent).toContain('T-001');

    const link = html.querySelector('a[href*="/projects/alpha/work-items/w1/review"]') as HTMLAnchorElement | null;
    expect(link).toBeTruthy();
    expect(link!.getAttribute('href')).toContain('taskId=T-001');
  });

  it('row without taskId has no — suffix and no taskId query param', () => {
    stub.list.set([
      {
        projectId: 'p1', projectSlug: 'alpha',
        workItemId: 'w1', workItemTitle: 'Plain task',
        checkpointKey: 'approve', checkpointDisplayName: 'Approve',
        raisedAt: '2026-05-01T10:00:00Z',
      },
    ]);
    stub.count.set(1);

    const fixture = TestBed.createComponent(PendingActionList);
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Plain task');
    expect(html.textContent).not.toContain('—');
    const link = html.querySelector('a[href*="/projects/alpha/work-items/w1/review"]') as HTMLAnchorElement | null;
    expect(link!.getAttribute('href')).not.toContain('taskId=');
  });

  it('two pending rows for the same work item with different task ids render as distinct rows', () => {
    stub.list.set([
      {
        projectId: 'p1', projectSlug: 'alpha',
        workItemId: 'w1', workItemTitle: 'Multi-task',
        checkpointKey: 'assignment-confirmed', checkpointDisplayName: 'Confirm task assignment',
        raisedAt: '2026-05-01T10:00:00Z', taskId: 'T-001',
      },
      {
        projectId: 'p1', projectSlug: 'alpha',
        workItemId: 'w1', workItemTitle: 'Multi-task',
        checkpointKey: 'assignment-confirmed', checkpointDisplayName: 'Confirm task assignment',
        raisedAt: '2026-05-01T10:01:00Z', taskId: 'T-002',
      },
    ]);
    stub.count.set(2);

    const fixture = TestBed.createComponent(PendingActionList);
    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    const items = html.querySelectorAll('ul.bg-white > li');
    expect(items.length).toBe(2);
    expect(html.textContent).toContain('T-001');
    expect(html.textContent).toContain('T-002');
  });

  it('shows the Reconnect affordance when disconnected and triggers store.reconnect on click', () => {
    stub.connected.set(false);
    const fixture = TestBed.createComponent(PendingActionList);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('disconnected');

    const reconnectBtn = (fixture.nativeElement as HTMLElement)
      .querySelector('button') as HTMLButtonElement | null;
    expect(reconnectBtn).toBeTruthy();
    reconnectBtn!.click();
    expect(stub.reconnect).toHaveBeenCalled();
  });
});
