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
  ExecutorDto,
  ReplaceContractsRequest,
} from '../../../core/api/executor-registry.types';
import type { RoleDto } from '../../../core/api/workspace.types';
import type { AppError } from '../../../core/errors/app-error';
import { AppButton, AppErrorBanner, AppModal } from '../../../shared';

interface ContractGroup {
  checkpointKey: FormControl<string>;
  displayName: FormControl<string>;
  requiredRoleKey: FormControl<string>;
  allowedOutcomes: FormControl<string>;
}

@Component({
  selector: 'contracts-form-modal',
  standalone: true,
  imports: [ReactiveFormsModule, AppModal, AppButton, AppErrorBanner],
  templateUrl: './contracts-form.modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContractsFormModal {
  readonly open = input<boolean>(false);
  readonly executor = input<ExecutorDto | null>(null);
  readonly availableRoles = input<RoleDto[]>([]);
  readonly working = input<boolean>(false);
  readonly serverError = input<AppError | null>(null);

  @Output() readonly submitted = new EventEmitter<ReplaceContractsRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  protected readonly form = new FormGroup({
    contracts: new FormArray<FormGroup<ContractGroup>>([]),
  });
  protected readonly submittedFlag = signal(false);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const e = this.executor();
      const contracts = this.form.controls.contracts;
      contracts.clear();
      if (e && e.checkpointContracts.length > 0) {
        for (const c of e.checkpointContracts) {
          contracts.push(make(c.checkpointKey, c.displayName, c.requiredRoleKey, c.allowedOutcomes.join(', ')));
        }
      } else {
        contracts.push(make());
      }
      this.submittedFlag.set(false);
    });
  }

  protected get contractsArray(): FormArray<FormGroup<ContractGroup>> { return this.form.controls.contracts; }

  protected addContract(): void { this.contractsArray.push(make()); }
  protected removeContract(i: number): void { this.contractsArray.removeAt(i); }

  protected onSubmit(): void {
    this.submittedFlag.set(true);
    if (this.form.invalid || this.contractsArray.length === 0) {
      this.form.markAllAsTouched();
      return;
    }
    const parsed: CreateCheckpointContractRequest[] = this.contractsArray.controls.map(g => {
      const v = g.getRawValue();
      return {
        checkpointKey: (v.checkpointKey ?? '').trim(),
        displayName: (v.displayName ?? '').trim(),
        requiredRoleKey: (v.requiredRoleKey ?? '').trim(),
        allowedOutcomes: (v.allowedOutcomes ?? '')
          .split(',').map(s => s.trim().toLowerCase()).filter(s => s.length > 0),
      };
    });
    this.submitted.emit({ checkpointContracts: parsed });
  }

  protected onCancel(): void {
    if (this.working()) return;
    this.cancelled.emit();
  }
}

function make(checkpointKey = '', displayName = '', requiredRoleKey = '', allowedOutcomes = ''): FormGroup<ContractGroup> {
  return new FormGroup<ContractGroup>({
    checkpointKey: new FormControl(checkpointKey, { nonNullable: true, validators: [Validators.required] }),
    displayName: new FormControl(displayName, { nonNullable: true, validators: [Validators.required] }),
    requiredRoleKey: new FormControl(requiredRoleKey, { nonNullable: true, validators: [Validators.required] }),
    allowedOutcomes: new FormControl(allowedOutcomes, { nonNullable: true, validators: [Validators.required] }),
  });
}
