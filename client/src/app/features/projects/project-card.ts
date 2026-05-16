import { ChangeDetectionStrategy, Component, EventEmitter, HostBinding, HostListener, input, Output } from '@angular/core';
import type { ProjectDto } from '../../core/api/workspace.types';

@Component({
  selector: 'project-card',
  standalone: true,
  templateUrl: './project-card.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectCard {
  readonly project = input.required<ProjectDto>();

  @Output() readonly opened = new EventEmitter<ProjectDto>();

  @HostBinding('class')
  protected get hostClass(): string {
    return 'block bg-white rounded-xl shadow-sm p-6 cursor-pointer ' +
      'hover:shadow-md hover:-translate-y-0.5 transition-all duration-200 ' +
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-300';
  }

  @HostBinding('attr.role')
  protected readonly role = 'button';

  @HostBinding('attr.tabindex')
  protected readonly tabindex = '0';

  @HostListener('click')
  protected onClick(): void {
    this.opened.emit(this.project());
  }

  @HostListener('keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.opened.emit(this.project());
    }
  }
}
