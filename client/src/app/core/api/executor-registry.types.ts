/**
 * Frontend mirrors of DevHub.Modules.ExecutorRegistry.DTOs.
 * Keep in lockstep with docs/api-spec.md §Executor Registry.
 */

export type ExecutorStatus = 'Active' | 'Paused' | 'Retired';

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
}

export interface UpdateExecutorRequest {
  displayName?: string;
  baseUrl?: string;
  credentialsRef?: string;
  status?: ExecutorStatus;
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
