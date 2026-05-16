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

export interface StartWorkItemRequest { title: string; input: unknown; }
export interface SignalRequest { outcome: string; payload?: unknown; }

export interface CheckpointContractView {
  checkpointKey: string;
  displayName: string;
  requiredRoleKey: string;
  allowedOutcomes: string[];
  state: 'active' | 'not-active';
}
