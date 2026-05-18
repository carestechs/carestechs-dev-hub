/**
 * Frontend mirrors of DevHub.Modules.ExecutorRegistry.DTOs.
 * Keep in lockstep with docs/api-spec.md §Executor Registry.
 */

export type ExecutorStatus = 'Active' | 'Paused' | 'Retired';

/**
 * FEAT-010: selects the IExecutorHttpClient implementation on the backend.
 * - `'devhub'`: DevHub-native protocol (FakeExecutor + legacy registrations).
 * - `'orchestrator'`: carestechs-agent-orchestrator's /api/v1/runs API.
 */
export type ExecutorProtocol = 'devhub' | 'orchestrator';

export interface CheckpointContractDto {
  checkpointKey: string;
  displayName: string;
  requiredRoleKey: string;
  allowedOutcomes: string[];
}

export interface ExecutorDto {
  id: string;
  key: string;
  displayName: string;
  baseUrl: string;
  credentialsRef: string;
  status: ExecutorStatus;
  checkpointContracts: CheckpointContractDto[];
  createdAt: string;
  /** FEAT-010. Backend always sends; old rows backfill to `'devhub'`. */
  protocol: ExecutorProtocol;
}

export interface CreateCheckpointContractRequest {
  checkpointKey: string;
  displayName: string;
  requiredRoleKey: string;
  allowedOutcomes: string[];
}

export interface CreateExecutorRequest {
  key: string;
  displayName: string;
  baseUrl: string;
  credentialsRef: string;
  checkpointContracts: CreateCheckpointContractRequest[];
  /** FEAT-010. Optional; backend defaults to `'devhub'` when omitted. */
  protocol?: ExecutorProtocol;
}

export interface UpdateExecutorRequest {
  displayName?: string;
  baseUrl?: string;
  credentialsRef?: string;
  status?: ExecutorStatus;
  /** FEAT-010. */
  protocol?: ExecutorProtocol;
}

export interface ReplaceContractsRequest {
  checkpointContracts: CreateCheckpointContractRequest[];
}

export interface ExecutorBindingDto {
  id: string;
  projectType: string;
  executorId: string;
  executorKey: string;
  executorDisplayName: string;
  executorStatus: ExecutorStatus;
  createdAt: string;
}

export interface CreateBindingRequest {
  projectType: string;
  executorId: string;
}
