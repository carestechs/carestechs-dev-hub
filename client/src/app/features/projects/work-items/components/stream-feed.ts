import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  effect,
  inject,
  input,
  OnDestroy,
  Output,
  signal,
} from '@angular/core';

interface StreamEvent { id: number; raw: string; receivedAt: Date; }

/**
 * SSE pass-through feed. Opens an EventSource against the work-item /stream endpoint with the
 * access token threaded through the query string (matches the FEAT-004 T-037 JWT shim — see
 * docs/api-spec.md §Live Stream).
 */
@Component({
  selector: 'stream-feed',
  standalone: true,
  templateUrl: './stream-feed.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StreamFeed implements OnDestroy {
  /** Full URL including query string. Setting it to null/undefined closes any open stream. */
  readonly streamUrl = input<string | null>(null);
  /** Cap the in-memory event list. Older entries are dropped. */
  readonly maxEvents = input<number>(200);

  @Output() readonly checkpointResolved = new EventEmitter<string | null>();

  protected readonly events = signal<StreamEvent[]>([]);
  protected readonly connected = signal(false);
  protected readonly errored = signal(false);

  private source: EventSource | null = null;
  private nextId = 0;
  private destroyRef = inject(DestroyRef);

  constructor() {
    effect(() => {
      const url = this.streamUrl();
      this.close();
      if (url) this.open(url);
    });
    this.destroyRef.onDestroy(() => this.close());
  }

  protected reconnect(): void {
    const url = this.streamUrl();
    this.close();
    if (url) this.open(url);
  }

  ngOnDestroy(): void { this.close(); }

  private open(url: string): void {
    this.errored.set(false);
    this.connected.set(false);
    try {
      this.source = new EventSource(url);
    } catch {
      this.errored.set(true);
      return;
    }
    this.source.onopen = () => this.connected.set(true);
    this.source.onmessage = ev => {
      const raw = ev.data ?? '';
      this.events.update(arr => {
        const next = arr.concat({ id: this.nextId++, raw, receivedAt: new Date() });
        return next.length > this.maxEvents() ? next.slice(-this.maxEvents()) : next;
      });
      // Heuristic: if the executor publishes a "checkpoint-resolved" event, surface it.
      if (raw.includes('checkpoint-resolved') || raw.includes('completed')) {
        this.checkpointResolved.emit(raw);
      }
    };
    this.source.onerror = () => {
      this.connected.set(false);
      this.errored.set(true);
    };
  }

  private close(): void {
    if (this.source) {
      this.source.close();
      this.source = null;
    }
    this.connected.set(false);
  }
}
