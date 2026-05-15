import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { problemDetailsInterceptor } from './core/errors/problem-details.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // Order matters: authInterceptor handles 401 refresh+replay, then
    // problemDetailsInterceptor normalizes any final error body.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, problemDetailsInterceptor])),
    // Run silent restore-from-refresh-cookie before any route guard sees auth state.
    provideAppInitializer(() => inject(AuthService).restore()),
  ],
};
