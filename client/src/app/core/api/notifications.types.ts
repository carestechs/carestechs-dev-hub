/**
 * Frontend mirror of DevHub.Modules.Notifications.DTOs.PendingActionDto.
 * Keep in lockstep with docs/api-spec.md §Notifications.
 */
export interface PendingActionDto {
  projectId: string;
  projectSlug: string;
  workItemId: string;
  workItemTitle: string;
  checkpointKey: string;
  checkpointDisplayName: string;
  raisedAt: string;
  /** Set when the active contract is per-task (FEAT-009). Disambiguates loop-back rows. */
  taskId?: string | null;
}

/** Shape of one event delivered over the SSE stream. */
export interface PendingActionEvent {
  kind: 'raised' | 'dismissed';
  memberId: string;
  projectId: string;
  workItemId: string;
  checkpointKey: string;
  occurredAt: string;
  /** Set when the active contract is per-task (FEAT-009). */
  taskId?: string | null;
}
