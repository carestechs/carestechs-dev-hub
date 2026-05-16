import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { PendingActionsStore } from './pending-actions.store';

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly url: string;
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  constructor(url: string) { this.url = url; FakeEventSource.instances.push(this); }
  close(): void { this.closed = true; }

  emit(json: object): void { this.onmessage?.({ data: JSON.stringify(json) }); }
  fail(): void { this.onerror?.(); }
  open(): void { this.onopen?.(); }
}

describe('PendingActionsStore', () => {
  let store: PendingActionsStore;
  let mock: HttpTestingController;
  let originalEventSource: any;

  beforeEach(() => {
    originalEventSource = (globalThis as any).EventSource;
    (globalThis as any).EventSource = FakeEventSource;
    FakeEventSource.instances = [];

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    mock = TestBed.inject(HttpTestingController);

    // Pretend we're logged in with a token so openStream() proceeds.
    const auth = TestBed.inject(AuthService);
    (auth as any)._token?.set?.('test-token');
    if (auth.token() === null) {
      // Fallback for stores that gate on a non-null token.
      Object.defineProperty(auth, 'token', { value: () => 'test-token', configurable: true });
    }
    store = TestBed.inject(PendingActionsStore);
  });

  afterEach(() => { (globalThis as any).EventSource = originalEventSource; });

  function flushPending(items: { wid: string; cp: string; title: string; slug: string }[]): void {
    const req = mock.expectOne('/api/notifications/pending');
    req.flush({
      data: items.map(i => ({
        projectId: 'p1', projectSlug: i.slug,
        workItemId: i.wid, workItemTitle: i.title,
        checkpointKey: i.cp, checkpointDisplayName: i.cp,
        raisedAt: '2026-05-01T00:00:00Z',
      })),
    });
  }

  it('starts: fetches /pending and opens EventSource', async () => {
    const startTask = store.start();
    flushPending([{ wid: 'w1', cp: 'approve', title: 'One', slug: 'alpha' }]);
    await startTask;

    expect(store.count()).toBe(1);
    expect(store.list()[0].workItemId).toBe('w1');
    expect(FakeEventSource.instances.length).toBe(1);
  });

  it('badgeText: hides at 0, raw count under 100, "99+" overflow at 100+', async () => {
    const startTask = store.start();
    flushPending([]);
    await startTask;
    expect(store.badgeText()).toBeNull();

    // Inject items via the internal signal to avoid 100 HTTP round-trips.
    const list = (store as any)._list;
    list.set(Array.from({ length: 7 }, (_, i) => ({
      projectId: 'p', projectSlug: 's', workItemId: 'w' + i,
      workItemTitle: 't', checkpointKey: 'c', checkpointDisplayName: 'c', raisedAt: '',
    })));
    expect(store.badgeText()).toBe('7');

    list.set(Array.from({ length: 250 }, (_, i) => ({
      projectId: 'p', projectSlug: 's', workItemId: 'w' + i,
      workItemTitle: 't', checkpointKey: 'c', checkpointDisplayName: 'c', raisedAt: '',
    })));
    expect(store.badgeText()).toBe('99+');
  });

  it('handles dismissed events by removing the matching entry without refetching', async () => {
    const startTask = store.start();
    flushPending([
      { wid: 'w1', cp: 'approve', title: 'One', slug: 'alpha' },
      { wid: 'w2', cp: 'review', title: 'Two', slug: 'alpha' },
    ]);
    await startTask;
    const before = FakeEventSource.instances[0]!;

    before.emit({ kind: 'dismissed', memberId: 'm', projectId: 'p',
      workItemId: 'w1', checkpointKey: 'approve', occurredAt: '' });

    // No new HTTP — the in-memory list mutated.
    expect(store.count()).toBe(1);
    expect(store.list()[0].workItemId).toBe('w2');
  });

  it('handles raised events by refetching /pending', async () => {
    const startTask = store.start();
    flushPending([{ wid: 'w1', cp: 'approve', title: 'One', slug: 'alpha' }]);
    await startTask;
    const source = FakeEventSource.instances[0]!;

    source.emit({ kind: 'raised', memberId: 'm', projectId: 'p',
      workItemId: 'w2', checkpointKey: 'review', occurredAt: '' });

    // The store fired a refresh — service the new /pending request.
    flushPending([
      { wid: 'w1', cp: 'approve', title: 'One', slug: 'alpha' },
      { wid: 'w2', cp: 'review', title: 'Two', slug: 'alpha' },
    ]);
    for (let i = 0; i < 4; i++) await Promise.resolve();
    expect(store.count()).toBe(2);
  });

  it('flips connected to false on onerror', async () => {
    const startTask = store.start();
    flushPending([]);
    await startTask;
    const source = FakeEventSource.instances[0]!;
    source.open();
    expect(store.connected()).toBeTrue();
    source.fail();
    expect(store.connected()).toBeFalse();
  });

  it('shutdown closes the stream and clears state', async () => {
    const startTask = store.start();
    flushPending([{ wid: 'w1', cp: 'approve', title: 'One', slug: 'alpha' }]);
    await startTask;
    const source = FakeEventSource.instances[0]!;

    store.shutdown();
    expect(source.closed).toBeTrue();
    expect(store.count()).toBe(0);
    expect(store.connected()).toBeFalse();
  });
});
