import { TestBed } from '@angular/core/testing';
import type { StartWorkItemRequest } from '../../../core/api/work-items.types';
import { StartWorkModal } from './start-work.modal';

describe('StartWorkModal', () => {
  function createOpen() {
    TestBed.configureTestingModule({ imports: [StartWorkModal] });
    const fixture = TestBed.createComponent(StartWorkModal);
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
});
