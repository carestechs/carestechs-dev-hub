import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuditService } from '../../core/api/audit.service';
import type { AuditEntryDto, AuditFilter } from '../../core/api/audit.types';
import type { PageMeta, PageRequest } from '../../core/api/workspace.types';
import { AuditTable } from './audit-table';

@Component({
  selector: 'operator-audit-page',
  standalone: true,
  imports: [RouterLink, AuditTable],
  templateUrl: './operator-audit.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperatorAuditPage {
  private readonly audit = inject(AuditService);

  protected readonly loading = signal(true);
  protected readonly rows = signal<AuditEntryDto[]>([]);
  protected readonly meta = signal<PageMeta | null>(null);
  protected readonly filter = signal<AuditFilter>({});
  protected readonly page = signal<PageRequest>({ page: 1, pageSize: 50 });
  protected readonly errored = signal(false);

  constructor() {
    void this.refresh();
  }

  private async refresh(): Promise<void> {
    this.loading.set(true);
    this.errored.set(false);
    try {
      const env = await this.audit.listAdmin(this.filter(), this.page());
      this.rows.set(env.data);
      this.meta.set(env.meta);
    } catch (e: unknown) {
      this.errored.set(e instanceof HttpErrorResponse);
    } finally {
      this.loading.set(false);
    }
  }

  protected async onFilterChanged(f: AuditFilter): Promise<void> {
    this.filter.set(f);
    this.page.update(p => ({ ...p, page: 1 }));
    await this.refresh();
  }

  protected async onPageChanged(p: { page: number; pageSize: number }): Promise<void> {
    this.page.update(prev => ({ ...prev, ...p }));
    await this.refresh();
  }
}
