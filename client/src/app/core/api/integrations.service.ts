import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Envelope } from './workspace.types';

export interface GitHubStatusDto {
  configured: boolean;
}

@Injectable({ providedIn: 'root' })
export class IntegrationsService {
  private readonly http = inject(HttpClient);

  getGitHubStatus(): Promise<GitHubStatusDto> {
    return firstValueFrom(
      this.http.get<GitHubStatusDto>('/api/integrations/github/status')
    );
  }
}
