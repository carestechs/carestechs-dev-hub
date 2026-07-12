import { ChangeDetectionStrategy, Component, EventEmitter, OnInit, Output, computed, input, signal } from '@angular/core';
import type { PanelSubmit } from './panel-submit';

interface TaskItem {
  id: string;
  title: string;
  kind?: string;
  description?: string;
}

@Component({
  selector: 'task-list-review-panel',
  standalone: true,
  templateUrl: './task-list-review-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskListReviewPanel implements OnInit {
  readonly artefact = input<unknown>(null);
  readonly canAct = input<boolean>(false);
  readonly working = input<boolean>(false);

  @Output() readonly submitted = new EventEmitter<PanelSubmit>();

  protected readonly editableTasks = signal<TaskItem[]>([]);
  protected readonly rejecting = signal(false);
  protected readonly adding = signal(false);
  protected readonly feedback = signal('');
  protected readonly newTitle = signal('');
  protected readonly newDescription = signal('');
  private addCounter = 0;

  protected readonly canSubmit = computed(() =>
    this.canAct() && !this.working() && this.editableTasks().length > 0);

  protected readonly canReject = computed(() =>
    this.canAct() && !this.working() && this.feedback().trim().length > 0);

  protected readonly canAdd = computed(() =>
    this.newTitle().trim().length > 0);

  protected readonly disabled = computed(() =>
    !this.canAct() || this.working());

  ngOnInit(): void {
    const raw = this.artefact();
    if (raw && typeof raw === 'object') {
      const obj = raw as Record<string, unknown>;
      const tasks = Array.isArray(obj['tasks']) ? obj['tasks'] : [];
      this.editableTasks.set(
        tasks
          .filter((t): t is Record<string, unknown> => t != null && typeof t === 'object')
          .map(t => ({
            id: typeof t['id'] === 'string' ? t['id'] : '',
            title: typeof t['title'] === 'string' ? t['title'] : '',
            kind: typeof t['kind'] === 'string' ? t['kind'] : undefined,
            description: typeof t['description'] === 'string' ? t['description'] : undefined,
          }))
      );
    }
  }

  protected updateTitle(index: number, value: string): void {
    this.editableTasks.update(tasks =>
      tasks.map((t, i) => i === index ? { ...t, title: value } : t));
  }

  protected updateDescription(index: number, value: string): void {
    this.editableTasks.update(tasks =>
      tasks.map((t, i) => i === index ? { ...t, description: value || undefined } : t));
  }

  protected moveUp(index: number): void {
    if (index <= 0) return;
    this.editableTasks.update(tasks => {
      const copy = [...tasks];
      [copy[index - 1], copy[index]] = [copy[index], copy[index - 1]];
      return copy;
    });
  }

  protected moveDown(index: number): void {
    this.editableTasks.update(tasks => {
      if (index >= tasks.length - 1) return tasks;
      const copy = [...tasks];
      [copy[index], copy[index + 1]] = [copy[index + 1], copy[index]];
      return copy;
    });
  }

  protected removeTask(index: number): void {
    this.editableTasks.update(tasks => tasks.filter((_, i) => i !== index));
  }

  protected addTask(): void {
    const title = this.newTitle().trim();
    if (!title) return;
    this.addCounter++;
    this.editableTasks.update(tasks => [
      ...tasks,
      { id: `T-NEW-${this.addCounter}`, title, description: this.newDescription().trim() || undefined },
    ]);
    this.newTitle.set('');
    this.newDescription.set('');
    this.adding.set(false);
  }

  protected onConfirm(): void {
    if (!this.canSubmit()) return;
    this.submitted.emit({
      outcome: 'confirmed',
      payload: { tasks: this.editableTasks() },
    });
  }

  protected onReject(): void {
    if (!this.canReject()) return;
    this.submitted.emit({
      outcome: 'rejected',
      payload: { feedback: this.feedback().trim() },
    });
  }

  protected toggleReject(): void {
    this.rejecting.update(v => !v);
    this.feedback.set('');
  }

  protected toggleAdd(): void {
    this.adding.update(v => !v);
    this.newTitle.set('');
    this.newDescription.set('');
  }
}
