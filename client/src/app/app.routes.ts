import { Component } from '@angular/core';
import { Routes } from '@angular/router';
import { anonGuard, authGuard } from './core/auth/auth.guard';

// Placeholder pages until T-015 (Login) and T-016 (Home) land.
@Component({
  standalone: true,
  template: `
    <div class="bg-white rounded-xl shadow-sm p-6 text-center">
      <h1 class="text-2xl font-bold mb-2">Sign in</h1>
      <p class="text-slate-500 text-sm">Login screen lands in T-015.</p>
    </div>
  `,
})
class LoginPlaceholder {}

@Component({
  standalone: true,
  template: `
    <header class="mb-8">
      <h1 class="text-3xl font-bold">Welcome to DevHub</h1>
      <p class="text-slate-500 mt-2">Authenticated home — full Home page lands in T-016.</p>
    </header>
  `,
})
class HomePlaceholder {}

@Component({
  standalone: true,
  template: `
    <header class="mb-8">
      <h1 class="text-3xl font-bold">Profile</h1>
      <p class="text-slate-500 mt-2">Profile + sign-out land alongside Login (T-015).</p>
    </header>
  `,
})
class ProfilePlaceholder {}

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./core/layouts/public-layout/public-layout').then(m => m.PublicLayout),
    children: [
      { path: '', component: LoginPlaceholder },
    ],
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./core/layouts/app-shell/app-shell').then(m => m.AppShell),
    children: [
      { path: '', pathMatch: 'full', component: HomePlaceholder },
      { path: 'me', component: ProfilePlaceholder },
    ],
  },
  { path: '**', redirectTo: '' },
];
