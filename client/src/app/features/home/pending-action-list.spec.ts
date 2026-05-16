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
