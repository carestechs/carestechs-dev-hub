import { Routes } from '@angular/router';
import { anonGuard, authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./core/layouts/public-layout/public-layout').then(m => m.PublicLayout),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/login/login.page').then(m => m.LoginPage),
      },
    ],
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./core/layouts/app-shell/app-shell').then(m => m.AppShell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/home/home.page').then(m => m.HomePage),
      },
      {
        path: 'projects',
        loadComponent: () => import('./features/projects/project-list.page').then(m => m.ProjectListPage),
      },
      {
        path: 'projects/:slug',
        loadComponent: () => import('./features/projects/project-home.page').then(m => m.ProjectHomePage),
      },
      {
        path: 'me',
        loadComponent: () => import('./features/profile/profile.page').then(m => m.ProfilePage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
