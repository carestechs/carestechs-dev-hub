import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  computed,
  effect,
  input,
  Output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type { ExecutorProtocol } from '../../../core/api/executor-registry.types';
import type { StartWorkItemRequest } from '../../../core/api/work-items.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../../shared';

// IMP-001. Static intake examples; the modal pre-fills the JSON textarea (and
// its placeholder) with the entry that matches the bound executor's protocol.
// Source of truth for each shape:
//   - 'orchestrator' → carestechs-agent-orchestrator/src/app/modules/ai/schemas.py § RunCreateRequest
//   - 'devhub'       → IExecutorHttpClient (FakeExecutor) — payload is opaque pass-through
const EXAMPLE_PAYLOADS: Record<ExecutorProtocol, string> = {
  devhub: '{}',
  orchestrator: `{
  "id": "FEAT-001",
  "kind": "FEAT",
  "content": "# Feature Brief\\n\\nDescribe the work item here.\\n\\n## Acceptance Criteria\\n\\n- [ ] Criterion 1"
}`,
};
const DEFAULT_PROTOCOL: ExecutorProtocol = 'orchestrator';

@Component({
  selector: 'start-work-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './start-work.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StartWorkModal {
  readonly open = input<boolean>(false);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);
  /** IMP-001. Bound executor's protocol; drives which example payload pre-fills the textarea. */
  readonly protocol = input<ExecutorProtocol | null>(null);
  /** Project slug — used to build the Docs tab link on docs-incomplete errors. */
  readonly projectSlug = input<string>('');

  protected readonly isDocsIncomplete = computed(
    () => this.serverError()?.type === '/probs/project-docs-incomplete',
  );

  @Output() readonly submitted = new EventEmitter<StartWorkItemRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();
  @Output() readonly goToDocs = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    title: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(255)],
    }),
    inputJson: new FormControl<string>('{}', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected readonly submittedFlag = signal(false);
  protected readonly jsonError = signal<string | null>(null);
  protected readonly exampleJson = computed(
    () => EXAMPLE_PAYLOADS[this.protocol() ?? DEFAULT_PROTOCOL],
  );

  constructor() {
    effect(() => {
      if (!this.open()) return;
      this.form.reset({ title: '', inputJson: this.exampleJson() });
      this.submittedFlag.set(false);
      this.jsonError.set(null);
    });
  }

  protected titleError(): string | null {
    const c = this.form.controls.title;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Title is required.';
    if (c.errors?.['maxlength']) return 'Title is too long.';
    return null;
  }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    this.jsonError.set(null);

    if (this.form.controls.title.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.controls.inputJson.value.trim();
    let parsed: unknown;
    try {
      parsed = raw.length === 0 ? {} : JSON.parse(raw);
    } catch (err) {
      this.jsonError.set('Input is not valid JSON.');
      return;
    }

    this.submitted.emit({ title: this.form.controls.title.value, input: parsed });
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }

  protected onGoToDocs(): void {
    this.cancelled.emit();
    this.goToDocs.emit();
  }
}
