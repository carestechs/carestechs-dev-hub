import { TestBed } from '@angular/core/testing';
import type { ExecutorProtocol } from '../../../core/api/executor-registry.types';
import type { StartWorkItemRequest } from '../../../core/api/work-items.types';
import { StartWorkModal } from './start-work.modal';

describe('StartWorkModal', () => {
  function createOpen(protocol?: ExecutorProtocol) {
    TestBed.configureTestingModule({ imports: [StartWorkModal] });
    const fixture = TestBed.createComponent(StartWorkModal);
    if (protocol) fixture.componentRef.setInput('protocol', protocol);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    return fixture;
  }

  it('requires title and rejects invalid JSON', () => {
    const fixture = createOpen();
    const emitted: StartWorkItemRequest[] = [];
    fixture.componentInstance.submitted.subscribe(r => emitted.push(r));

    const cmp = fixture.componentInstance as unknown as { onSubmit(): void; form: any };
    // Empty title → no emit.
    cmp.form.controls.title.setValue('');
    cmp.form.controls.inputJson.setValue('{}');
    cmp.onSubmit();
    expect(emitted.length).toBe(0);

    // Invalid JSON → no emit.
    cmp.form.controls.title.setValue('Demo');
    cmp.form.controls.inputJson.setValue('{ broken');
    cmp.onSubmit();
    expect(emitted.length).toBe(0);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Input is not valid JSON.');
  });

  it('emits parsed payload on submit', () => {
    const fixture = createOpen();
    const emitted: StartWorkItemRequest[] = [];
    fixture.componentInstance.submitted.subscribe(r => emitted.push(r));

    const cmp = fixture.componentInstance as unknown as { onSubmit(): void; form: any };
    cmp.form.controls.title.setValue('My work');
    cmp.form.controls.inputJson.setValue('{"endpoint":"/x","format":"csv"}');
    cmp.onSubmit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].title).toBe('My work');
    expect(emitted[0].input).toEqual({ endpoint: '/x', format: 'csv' });
  });

  // IMP-001 — per-protocol example payloads.

  it('defaults to the orchestrator example when no protocol is provided', () => {
    const fixture = createOpen();
    const cmp = fixture.componentInstance as unknown as { form: any };
    expect(JSON.parse(cmp.form.controls.inputJson.value)).toEqual({
      task: 'Describe the task to run',
    });
  });

  it("uses the devhub example when protocol='devhub'", () => {
    const fixture = createOpen('devhub');
    const cmp = fixture.componentInstance as unknown as { form: any };
    expect(cmp.form.controls.inputJson.value).toBe('{}');
    const placeholder = (fixture.nativeElement as HTMLElement)
      .querySelector('textarea')
      ?.getAttribute('placeholder');
    expect(placeholder).toBe('{}');
  });

  it('placeholder mirrors the orchestrator example payload', () => {
    const fixture = createOpen('orchestrator');
    const placeholder = (fixture.nativeElement as HTMLElement)
      .querySelector('textarea')
      ?.getAttribute('placeholder');
    expect(placeholder).toContain('"task"');
    expect(placeholder).toContain('Describe the task to run');
  });

  it('submitting the unedited orchestrator example emits a valid parsed payload', () => {
    const fixture = createOpen('orchestrator');
    const emitted: StartWorkItemRequest[] = [];
    fixture.componentInstance.submitted.subscribe(r => emitted.push(r));

    const cmp = fixture.componentInstance as unknown as { onSubmit(): void; form: any };
    cmp.form.controls.title.setValue('Demo');
    cmp.onSubmit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].input).toEqual({ task: 'Describe the task to run' });
  });
});
