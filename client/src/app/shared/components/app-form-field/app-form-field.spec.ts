import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppFormField } from './app-form-field';

@Component({
  standalone: true,
  imports: [AppFormField],
  template: `
    <app-form-field
      [label]="label"
      [helperText]="helperText"
      [error]="error"
      [required]="required"
    >
      <input #input type="email" name="email" />
    </app-form-field>
  `,
})
class Host {
  label = 'Email';
  helperText: string | null = null;
  error: string | null = null;
  required = false;
}

describe('AppFormField', () => {
  it('renders the label and the projected input', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const html: HTMLElement = fixture.nativeElement;
    expect(html.querySelector('span')!.textContent).toContain('Email');
    expect(html.querySelector('input')).toBeTruthy();
  });

  it('shows the required marker when required=true', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.required = true;
    fixture.detectChanges();
    const html: HTMLElement = fixture.nativeElement;
    expect(html.querySelector('span span.text-red-500')!.textContent).toContain('*');
  });

  it('shows helper text when no error', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.helperText = "We'll never share your email.";
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain("We'll never share");
  });

  it('shows error and sets aria-invalid on the projected input', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.error = 'Invalid';
    fixture.detectChanges();

    const html: HTMLElement = fixture.nativeElement;
    expect(html.querySelector('.text-red-600')!.textContent).toContain('Invalid');
    expect(html.querySelector('input')!.getAttribute('aria-invalid')).toBe('true');
  });
});
