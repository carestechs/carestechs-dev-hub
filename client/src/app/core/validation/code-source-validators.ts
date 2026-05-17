import { AbstractControl, ValidationErrors } from '@angular/forms';

/**
 * Client-side mirrors of DevHub.Contracts.Validation.CodeSourceValidator (FEAT-008 / T-056).
 * Keep these rules in lockstep with the backend; any tightening upstream must land in the
 * same PR. Errors are surfaced as Angular form-level keys (e.g. `repoShape`, `branchDotDot`)
 * so callers can map them to user-facing messages.
 *
 * Both validators treat the empty string as "no value" → no error — the fields they back
 * are optional everywhere they're used today (FEAT-008 keeps repo/defaultBranch/workBranch
 * all nullable in v1).
 */

const REPO_PATTERN = /^[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+$/;

export function repoValidator(c: AbstractControl): ValidationErrors | null {
  const v = (c.value ?? '') as string;
  if (v === '') return null;
  if (!REPO_PATTERN.test(v)) return { repoShape: true };
  if (v.endsWith('.git')) return { repoGitSuffix: true };
  return null;
}

export function branchValidator(c: AbstractControl): ValidationErrors | null {
  const v = (c.value ?? '') as string;
  if (v === '') return null;
  if (v.startsWith('/')) return { branchLeadingSlash: true };
  if (v.includes('..')) return { branchDotDot: true };
  for (const ch of v) {
    const code = ch.charCodeAt(0);
    if (code < 0x20 || code === 0x7F) return { branchControlChar: true };
    if (/\s/.test(ch)) return { branchWhitespace: true };
  }
  return null;
}

/** Maps the validator's ValidationErrors keys to short, user-facing messages. */
export function branchErrorMessage(errors: ValidationErrors | null | undefined): string | null {
  if (!errors) return null;
  if (errors['maxlength']) return 'Branch is too long.';
  if (errors['branchLeadingSlash']) return "Branch must not start with '/'.";
  if (errors['branchDotDot']) return "Branch must not contain '..'.";
  if (errors['branchWhitespace']) return 'Branch must not contain whitespace.';
  if (errors['branchControlChar']) return 'Branch must not contain control characters.';
  return null;
}

export function repoErrorMessage(errors: ValidationErrors | null | undefined): string | null {
  if (!errors) return null;
  if (errors['maxlength']) return 'Repo is too long.';
  if (errors['repoShape']) return "Use 'owner/name' — no URL prefix, no whitespace, no leading slash.";
  if (errors['repoGitSuffix']) return "Drop the '.git' suffix.";
  return null;
}
