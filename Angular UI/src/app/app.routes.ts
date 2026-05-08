import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';
import { adminGuard, configGuard } from './guards/role.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'dashboard',
    loadComponent: () => import('./layout/layout.component').then(m => m.LayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'monitor', pathMatch: 'full' },
      {
        path: 'monitor',
        loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'incidents',
        loadComponent: () => import('./pages/incidents/incidents.component').then(m => m.IncidentsComponent)
      },
      {
        path: 'alerts',
        loadComponent: () => import('./pages/alerts/alerts.component').then(m => m.AlertsComponent)
      },
      {
        path: 'configuration',
        canActivate: [configGuard],
        loadComponent: () => import('./pages/configuration/configuration.component').then(m => m.ConfigurationComponent)
      },
      {
        path: 'analytics',
        canActivate: [adminGuard],
        loadComponent: () => import('./pages/analytics/analytics.component').then(m => m.AnalyticsComponent)
      }
    ]
  },
  { path: '**', redirectTo: '/login' }
];
