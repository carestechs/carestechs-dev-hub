import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { WorkspaceService } from '../../../core/api/workspace.service';
import type { DocTemplateVersionDto } from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, ConfirmDialog } from '../../../shared';

@Component({
  selector: 'doc-templates-page',
  standalone: true,
  imports: [AppButton, ConfirmDialog, DatePipe],
  templateUrl: './doc-templates.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocTemplatesPage {
  private readonly ws = inject(WorkspaceService);

  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly versions = signal<DocTemplateVersionDto[]>([]);

  // New version modal state
  protected readonly modalOpen = signal(false);
  protected readonly modalWorking = signal(false);
  protected readonly modalError = signal<AppError | null>(null);
  protected readonly sourceVersionId = signal<string | null>(null);
  protected readonly notes = signal('');

  // Activate confirm
  protected readonly toActivate = signal<DocTemplateVersionDto | null>(null);
  protected readonly activating = signal(false);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.versions.set(await this.ws.listDocTemplateVersions());
    } catch (e) {
      this.error.set(toAppError(e, 'Could not load template versions'));
    } finally {
      this.loading.set(false);
    }
  }

  protected openNewVersionModal(): void {
    const active = this.versions().find(v => v.isActive);
    this.sourceVersionId.set(active?.id ?? this.versions()[0]?.id ?? null);
    this.notes.set('');
    this.modalError.set(null);
    this.modalOpen.set(true);
  }

  protected onNotesInput(event: Event): void {
    this.notes.set((event.target as HTMLTextAreaElement).value);
  }

  protected onSourceVersionChange(event: Event): void {
    this.sourceVersionId.set((event.target as HTMLSelectElement).value || null);
  }

  protected async createVersion(): Promise<void> {
    const sourceId = this.sourceVersionId();
    if (!sourceId) return;
    this.modalWorking.set(true);
    this.modalError.set(null);
    try {
      const created = await this.ws.createDocTemplateVersion({ sourceVersionId: sourceId, notes: this.notes() || undefined });
      this.versions.update(vs => [created, ...vs]);
      this.modalOpen.set(false);
    } catch (e) {
      this.modalError.set(toAppError(e, 'Could not create version'));
    } finally {
      this.modalWorking.set(false);
    }
  }

  protected confirmActivate(version: DocTemplateVersionDto): void {
    this.toActivate.set(version);
  }

  protected async doActivate(): Promise<void> {
    const v = this.toActivate();
    if (!v) return;
    this.activating.set(true);
    try {
      const updated = await this.ws.activateDocTemplateVersion(v.id);
      this.versions.update(vs => vs.map(x => x.id === updated.id ? updated : { ...x, isActive: false }));
    } catch (e) {
      this.error.set(toAppError(e, 'Could not activate version'));
    } finally {
      this.activating.set(false);
      this.toActivate.set(null);
    }
  }
}

function toAppError(err: unknown, fallbackTitle: string): AppError {
  if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object' && 'title' in err.error) {
    return err.error as AppError;
  }
  return { type: 'about:blank', title: fallbackTitle, status: 0, detail: err instanceof Error ? err.message : 'An unexpected error occurred.' };
}
