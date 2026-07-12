import { ComponentRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TaskListReviewPanel } from './task-list-review-panel';

function makeArtefact(tasks: unknown[]) {
  return { tasks };
}

describe('TaskListReviewPanel — kind badge', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [TaskListReviewPanel] }).compileComponents());

  function mount(artefact: unknown) {
    const fixture = TestBed.createComponent(TaskListReviewPanel);
    const cref = fixture.componentRef as ComponentRef<TaskListReviewPanel>;
    cref.setInput('artefact', artefact);
    cref.setInput('canAct', true);
    cref.setInput('working', false);
    fixture.detectChanges();
    return fixture;
  }

  it('shows Mockup badge for kind="mockup" tasks', () => {
    const fixture = mount(makeArtefact([
      { id: 'T-001', title: 'Design login', kind: 'mockup' },
    ]));
    const badges = (fixture.nativeElement as HTMLElement).querySelectorAll('span');
    const badgeTexts = Array.from(badges).map(b => b.textContent?.trim());
    expect(badgeTexts).toContain('Mockup');
  });

  it('does not show Mockup badge for kind="feature" tasks', () => {
    const fixture = mount(makeArtefact([
      { id: 'T-002', title: 'Implement auth', kind: 'feature' },
    ]));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Mockup');
  });

  it('does not show Mockup badge when kind is absent', () => {
    const fixture = mount(makeArtefact([
      { id: 'T-003', title: 'Fix bug' },
    ]));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Mockup');
  });

  it('shows badge only on mockup tasks in a mixed list', () => {
    const fixture = mount(makeArtefact([
      { id: 'T-001', title: 'Design login', kind: 'mockup' },
      { id: 'T-002', title: 'Implement auth', kind: 'feature' },
      { id: 'T-003', title: 'Fix bug', kind: 'bug' },
    ]));
    const taskCards = (fixture.nativeElement as HTMLElement).querySelectorAll('.group');
    expect(taskCards.length).toBe(3);

    const mockupCard = taskCards[0];
    expect(mockupCard.textContent).toContain('Mockup');

    const featureCard = taskCards[1];
    const featureSpans = Array.from(featureCard.querySelectorAll('span')).map(s => s.textContent?.trim());
    expect(featureSpans).not.toContain('Mockup');

    const bugCard = taskCards[2];
    const bugSpans = Array.from(bugCard.querySelectorAll('span')).map(s => s.textContent?.trim());
    expect(bugSpans).not.toContain('Mockup');
  });
});
