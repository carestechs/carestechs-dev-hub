import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { WorkspaceService } from '../../core/api/workspace.service';
import type { ProjectDocDto, ProjectDocSectionDto } from '../../core/api/workspace.types';
import { AuthService } from '../../core/auth/auth.service';
import type { AppError } from '../../core/errors/app-error';
import { AppButton } from '../../shared';

@Component({
  selector: 'doc-editor-page',
  standalone: true,
  imports: [RouterLink, AppButton],
  templateUrl: './doc-editor.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocEditorPage {
  private readonly ws = inject(WorkspaceService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly isOperator = this.auth.isOperator;

  protected readonly loading = signal(true);
  protected readonly projectId = signal<string | null>(null);
  protected readonly projectName = signal<string | null>(null);
  protected readonly slug = signal('');
  protected readonly doc = signal<ProjectDocDto | null>(null);
  protected readonly errorKind = signal<'not-found' | 'forbidden' | 'other' | null>(null);
  protected readonly error = signal<AppError | null>(null);

  protected readonly saving = signal(false);
  protected readonly saveError = signal<AppError | null>(null);
  protected readonly savedOk = signal(false);

  protected readonly sectionValues = signal<Record<string, string>>({});

  protected readonly isLocked = computed(() => this.doc()?.locked ?? false);

  protected readonly isDirty = computed(() => {
    const d = this.doc();
    if (!d || d.locked) return false;
    const vals = this.sectionValues();
    return d.sections.some(s => (vals[s.key] ?? '') !== (s.content ?? ''));
  });

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(async params => {
      const slug = params.get('slug');
      const key = params.get('key');
      if (!slug || !key) return;
      this.slug.set(slug);
      await this.load(slug, key);
    });
  }

  private async load(slug: string, key: string): Promise<void> {
    this.loading.set(true);
    this.errorKind.set(null);
    this.error.set(null);
    this.doc.set(null);
    this.savedOk.set(false);
    try {
      const project = await this.ws.getProjectBySlug(slug);
      this.projectId.set(project.id);
      this.projectName.set(project.name);
      const doc = await this.ws.getDoc(project.id, key);
      this.doc.set(doc);
      this.initSectionValues(doc);
    } catch (e: unknown) {
      this.classify(e);
    } finally {
      this.loading.set(false);
    }
  }

  private initSectionValues(doc: ProjectDocDto): void {
    const vals: Record<string, string> = {};
    for (const s of doc.sections) vals[s.key] = s.content ?? '';
    this.sectionValues.set(vals);
  }

  protected onSectionInput(key: string, event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.sectionValues.update(prev => ({ ...prev, [key]: value }));
    this.savedOk.set(false);
  }

  protected sectionValue(section: ProjectDocSectionDto): string {
    return this.sectionValues()[section.key] ?? '';
  }

  protected async save(): Promise<void> {
    const pid = this.projectId();
    const doc = this.doc();
    if (!pid || !doc) return;
    this.saving.set(true);
    this.saveError.set(null);
    this.savedOk.set(false);
    try {
      const vals = this.sectionValues();
      const updated = await this.ws.upsertDocSections(pid, doc.docKey, { sections: { ...vals } });
      this.doc.set(updated);
      this.initSectionValues(updated);
      this.savedOk.set(true);
    } catch (e: unknown) {
      this.saveError.set(toAppError(e, 'Could not save document'));
    } finally {
      this.saving.set(false);
    }
  }

  protected cancel(): void {
    void this.router.navigate(['/projects', this.slug()], { fragment: 'docs' });
  }

  private classify(e: unknown): void {
    if (e instanceof HttpErrorResponse) {
      if (e.status === 404) { this.errorKind.set('not-found'); return; }
      if (e.status === 403) { this.errorKind.set('forbidden'); return; }
      if (e.error && typeof e.error === 'object' && 'title' in e.error) {
        this.error.set(e.error as AppError);
      } else {
        this.error.set({ type: 'about:blank', title: 'Could not load document', status: e.status, detail: e.message });
      }
      this.errorKind.set('other');
      return;
    }
    this.error.set({ type: 'about:blank', title: 'Could not load document', status: 0, detail: e instanceof Error ? e.message : 'An unexpected error occurred.' });
    this.errorKind.set('other');
  }
}

function toAppError(err: unknown, fallbackTitle: string): AppError {
  if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object' && 'title' in err.error) {
    return err.error as AppError;
  }
  return { type: 'about:blank', title: fallbackTitle, status: 0, detail: err instanceof Error ? err.message : 'An unexpected error occurred.' };
}
