import { TestBed } from '@angular/core/testing';
import type { ProjectDto } from '../../core/api/workspace.types';
import { ProjectCard } from './project-card';

const PROJECT: ProjectDto = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Add CSV export',
  slug: 'add-csv-export',
  projectType: 'feature-delivery',
  owningTeam: { id: 'team-1', name: 'Engineering' },
  description: undefined,
  inFlightWorkItems: 3,
  createdAt: '2026-05-01T00:00:00Z',
};

describe('ProjectCard', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ProjectCard] }).compileComponents();
  });

  function render(project: ProjectDto = PROJECT) {
    const fixture = TestBed.createComponent(ProjectCard);
    fixture.componentRef.setInput('project', project);
    fixture.detectChanges();
    return fixture;
  }

  it('renders name, slug, team chip, projectType chip', () => {
    const fixture = render();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Add CSV export');
    expect(html.textContent).toContain('add-csv-export');
    expect(html.textContent).toContain('Engineering');
    expect(html.textContent).toContain('feature-delivery');
    expect(html.textContent).toContain('3 in-flight items');
  });

  it('renders the singular "1 in-flight item" message', () => {
    const fixture = render({ ...PROJECT, inFlightWorkItems: 1 });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('1 in-flight item');
  });

  it('renders "No in-flight work" when count is 0', () => {
    const fixture = render({ ...PROJECT, inFlightWorkItems: 0 });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No in-flight work');
  });

  it('emits opened on click', () => {
    const fixture = render();
    let received: ProjectDto | undefined;
    fixture.componentInstance.opened.subscribe(p => received = p);
    (fixture.nativeElement as HTMLElement).click();
    expect(received).toBe(PROJECT);
  });

  it('emits opened on Enter and Space', () => {
    const fixture = render();
    let count = 0;
    fixture.componentInstance.opened.subscribe(() => count++);
    const host = fixture.nativeElement as HTMLElement;
    host.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    host.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));
    expect(count).toBe(2);
  });

  it('exposes role="button" and tabindex=0 for keyboard accessibility', () => {
    const fixture = render();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.getAttribute('role')).toBe('button');
    expect(host.getAttribute('tabindex')).toBe('0');
  });
});
