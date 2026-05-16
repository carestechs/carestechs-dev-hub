import { Routes } from '@angular/router';
import { anonGuard, authGuard } from './core/auth/auth.guard';
import { operatorGuard } from './core/auth/operator.guard';

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
        path: 'projects/:slug/work-items/:id',
        loadComponent: () =>
          import('./features/projects/work-items/work-item-detail.page').then(m => m.WorkItemDetailPage),
      },
      {
        path: 'projects/:slug/work-items/:id/review',
        loadComponent: () =>
          import('./features/projects/work-items/review.page').then(m => m.ReviewPage),
      },
      {
        path: 'projects/:slug/audit',
        loadComponent: () =>
          import('./features/audit/project-audit.page').then(m => m.ProjectAuditPage),
      },
      {
        path: 'operator/audit',
        canActivate: [operatorGuard],
        loadComponent: () =>
          import('./features/audit/operator-audit.page').then(m => m.OperatorAuditPage),
      },
      {
        path: 'projects/:slug/admin/memberships',
        canActivate: [operatorGuard],
        loadComponent: () =>
          import('./features/projects/memberships/memberships.page').then(m => m.MembershipsPage),
      },
      {
        path: 'admin/teams',
        canActivate: [operatorGuard],
        loadComponent: () => import('./features/admin/teams/teams.page').then(m => m.TeamsPage),
      },
      {
        path: 'admin/members',
        canActivate: [operatorGuard],
        loadComponent: () => import('./features/admin/members/members.page').then(m => m.MembersPage),
      },
      {
        path: 'admin/executors',
        canActivate: [operatorGuard],
        loadComponent: () => import('./features/admin/executors/executors.page').then(m => m.ExecutorsPage),
      },
      {
        path: 'admin/executor-bindings',
        canActivate: [operatorGuard],
        loadComponent: () => import('./features/admin/executor-bindings/executor-bindings.page').then(m => m.ExecutorBindingsPage),
      },
      {
        path: 'me',
        loadComponent: () => import('./features/profile/profile.page').then(m => m.ProfilePage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
