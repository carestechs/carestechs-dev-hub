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
  /**
   * The orchestrator's run id (FEAT-010). Populated after Start succeeds when the bound
   * executor speaks the `'orchestrator'` protocol; null for devhub-protocol executors.
   * Surfaced on the WorkItem detail header for cross-system debugging.
   */
  executorRunId?: string | null;
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

/**
 * FEAT-011: a single step from executorState.steps, built from the orchestrator's
 * trace records. Human checkpoint steps have a non-null key; internal steps
 * (LLM, engine) have key: null.
 */
export interface ExecutorStep {
  key: string | null;
  nodeName: string;
  label: string;
  stepNumber: number;
  status: 'completed' | 'active' | 'pending' | 'failed';
  nodeInputs: Record<string, unknown> | null;
  nodeResult: Record<string, unknown> | null;
}

/**
 * FEAT-011: typed view of executorState for lifecycle-agent runs.
 * The raw executorState is opaque (unknown); this type is for panels
 * that understand the lifecycle-agent shape.
 */
export interface LifecycleExecutorState {
  runId: string;
  agentRef: string;
  steps: ExecutorStep[];
  assignments: Record<string, string>;
  stopReason: string | null;
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
