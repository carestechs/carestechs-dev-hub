import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  effect,
  input,
  Output,
  signal,
} from '@angular/core';
import {
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import type {
  CreateCheckpointContractRequest,
  CreateExecutorRequest,
  ExecutorDto,
  ExecutorStatus,
  UpdateExecutorRequest,
} from '../../../core/api/executor-registry.types';
import type { RoleDto } from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppFormField, AppModal } from '../../../shared';

type CreateOrUpdate = CreateExecutorRequest | UpdateExecutorRequest;

@Component({
  selector: 'executor-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner, AppFormField],
  templateUrl: './executor-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExecutorFormModal {
  readonly open = input<boolean>(false);
  readonly editing = input<ExecutorDto | null>(null);
  readonly availableRoles = input<RoleDto[]>([]);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);

  @Output() readonly submitted = new EventEmitter<CreateOrUpdate>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    key: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(60)],
    }),
    displayName: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    baseUrl: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^https?:\/\/.+/)],
    }),
    credentialsRef: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    status: new FormControl<ExecutorStatus>('Active', { nonNullable: true }),
    contracts: new FormArray<FormGroup<ContractGroup>>([]),
  });

  protected readonly submittedFlag = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const e = this.editing();
      const contracts = this.form.controls.contracts;
      contracts.clear();
      if (e) {
        for (const c of e.checkpointContracts) {
          contracts.push(makeContractGroup(c.checkpointKey, c.displayName, c.requiredRoleKey, c.allowedOutcomes.join(', ')));
        }
        this.form.reset({
          key: e.key,
          displayName: e.displayName,
          baseUrl: e.baseUrl,
          credentialsRef: e.credentialsRef,
          status: e.status,
        });
        this.form.controls.key.disable({ emitEvent: false });
      } else {
        contracts.push(makeContractGroup());
        this.form.reset({ key: '', displayName: '', baseUrl: '', credentialsRef: '', status: 'Active' });
        this.form.controls.key.enable({ emitEvent: false });
      }
      this.submittedFlag.set(false);
    });
  }

  protected get title(): string { return this.editing() ? 'Edit executor' : 'Register executor'; }
  protected get submitLabel(): string { return this.editing() ? 'Save' : 'Register'; }
  protected get isEdit(): boolean { return this.editing() !== null; }

  protected get contractsArray(): FormArray<FormGroup<ContractGroup>> { return this.form.controls.contracts; }

  protected addContract(): void { this.contractsArray.push(makeContractGroup()); }
  protected removeContract(i: number): void { this.contractsArray.removeAt(i); }

  protected keyError(): string | null { return errorText(this.form.controls.key, this.submittedFlag(), 'Key is required.'); }
  protected displayNameError(): string | null { return errorText(this.form.controls.displayName, this.submittedFlag(), 'Display name is required.'); }
  protected baseUrlError(): string | null {
    const c = this.form.controls.baseUrl;
    if (!(c.touched || this.submittedFlag()) || c.valid) return null;
    if (c.errors?.['required']) return 'Base URL is required.';
    if (c.errors?.['pattern']) return 'Base URL must start with http:// or https://';
    return null;
  }
  protected credentialsRefError(): string | null { return errorText(this.form.controls.credentialsRef, this.submittedFlag(), 'Credentials ref is required.'); }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const raw = this.form.getRawValue();
    const parsedContracts: CreateCheckpointContractRequest[] = raw.contracts.map(c => ({
      checkpointKey: (c.checkpointKey ?? '').trim(),
      displayName: (c.displayName ?? '').trim(),
      requiredRoleKey: (c.requiredRoleKey ?? '').trim(),
      allowedOutcomes: (c.allowedOutcomes ?? '')
        .split(',').map(s => s.trim().toLowerCase()).filter(s => s.length > 0),
    }));

    if (this.isEdit) {
      const body: UpdateExecutorRequest = {
        displayName: raw.displayName,
        baseUrl: raw.baseUrl,
        credentialsRef: raw.credentialsRef,
        status: raw.status,
      };
      this.submitted.emit(body);
    } else {
      const body: CreateExecutorRequest = {
        key: raw.key,
        displayName: raw.displayName,
        baseUrl: raw.baseUrl,
        credentialsRef: raw.credentialsRef,
        checkpointContracts: parsedContracts,
      };
      this.submitted.emit(body);
    }
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}

interface ContractGroup {
  checkpointKey: FormControl<string>;
  displayName: FormControl<string>;
  requiredRoleKey: FormControl<string>;
  allowedOutcomes: FormControl<string>;
}

function makeContractGroup(
  checkpointKey = '', displayName = '', requiredRoleKey = '', allowedOutcomes = '',
): FormGroup<ContractGroup> {
  return new FormGroup<ContractGroup>({
    checkpointKey: new FormControl(checkpointKey, { nonNullable: true, validators: [Validators.required, Validators.maxLength(60)] }),
    displayName: new FormControl(displayName, { nonNullable: true, validators: [Validators.required, Validators.maxLength(120)] }),
    requiredRoleKey: new FormControl(requiredRoleKey, { nonNullable: true, validators: [Validators.required] }),
    allowedOutcomes: new FormControl(allowedOutcomes, { nonNullable: true, validators: [Validators.required] }),
  });
}

function errorText(c: FormControl, submitted: boolean, requiredMsg: string): string | null {
  if (!(c.touched || submitted) || c.valid) return null;
  if (c.errors?.['required']) return requiredMsg;
  if (c.errors?.['maxlength']) return 'Value is too long.';
  return null;
}
