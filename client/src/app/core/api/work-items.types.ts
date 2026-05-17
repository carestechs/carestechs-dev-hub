/**
 * Frontend mirrors of DevHub.Modules.WorkItems.DTOs.
 * Keep in lockstep with docs/api-spec.md §Work Items.
 */

export type WorkItemStatus = string; // executor-defined; treat as opaque string

export interface ExecutorRef { id: string; key: string; displayName: string; }
export interface MemberRef { id: string; displayName: string; }

export interface WorkItemSummaryDto {
  id: string;
  projectId: string;
  title: string;
  currentStatus: WorkItemStatus;
  currentCheckpointKey?: string;
  executor: ExecutorRef;
  executorCorrelationMarker: string;
  createdAt: string;
  createdBy: MemberRef;
  /** Optional per-work-item branch override; falls back to project.defaultBranch (FEAT-008). */
  workBranch?: string | null;
  /**
   * The executor's current task identifier when the active checkpoint is per-task (FEAT-009).
   * Cached on every transition; null when the executor doesn't surface a task or the contract
   * is not per-task.
   */
  currentTaskId?: string | null;
}

export interface WorkItemDto extends WorkItemSummaryDto {
  executorState: unknown;
}

export interface CheckpointSignalDto {
  id: string;
  checkpointKey: string;
  outcome: string;
  signaledBy: MemberRef;
  signaledAt: string;
  executorResponseStatus?: number;
  payload?: unknown;
}

export interface StartWorkItemRequest {
  title: string;
  input: unknown;
  /** Optional per-work-item override of the project's defaultBranch (FEAT-008). */
  workBranch?: string;
}
export interface SignalRequest {
  outcome: string;
  payload?: unknown;
  /**
   * Identifier of the task this signal targets (FEAT-009). Required by the executor when the
   * active contract is per-task; DevHub forwards it verbatim. Omit when the contract is not
   * per-task.
   */
  taskId?: string;
}

/**
 * PATCH payload for /api/projects/{pid}/work-items/{wid}. v1 surface is workBranch-only.
 *
 * Three-valued semantics on `workBranch`:
 * - `undefined` (or property omitted) → leave existing value unchanged.
 * - `""` (empty string) → clear the override; falls back to project.defaultBranch.
 * - any other string → validated server-side and persisted.
 */
export interface UpdateWorkItemRequest {
  workBranch?: string | null;
}

export interface CheckpointContractView {
  checkpointKey: string;
  displayName: string;
  requiredRoleKey: string;
  allowedOutcomes: string[];
  state: 'active' | 'not-active';
  /**
   * When true, DevHub raises per-task pending rows (FEAT-009). T-070's review page swaps in
   * the AssignmentConfirmPanel when this is true AND checkpointKey === 'assignment-confirmed'.
   */
  perTask?: boolean;
}
