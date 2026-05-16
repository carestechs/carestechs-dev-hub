/**
 * Frontend mirror of DevHub.Modules.Audit.DTOs.AuditEntryDto + AuditFilter.
 * Keep in lockstep with docs/api-spec.md §AuditEntryDto.
 */
export type AuditOutcome = 'Granted' | 'Denied' | 'Failed';

export interface AuditActorDto {
  id: string;
  displayName: string;
}

export interface AuditEntryDto {
  id: string;
  occurredAt: string;
  actingMember?: AuditActorDto;
  projectId?: string;
  targetType: string;
  targetId?: string;
  action: string;
  outcome: AuditOutcome;
  reason?: string;
  details?: unknown;
}

export interface AuditFilter {
  actingMemberId?: string;
  targetType?: string;
  action?: string;
  outcome?: AuditOutcome;
  projectId?: string;
  from?: string;
  to?: string;
}
