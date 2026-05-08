import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { User, LoginCredentials } from '../models';
import { environment } from '../../environments/environment';

interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  user: User;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly USER_KEY  = 'cctv_user';
  private readonly TOKEN_KEY = 'cctv_token';
  private readonly REFRESH_KEY = 'cctv_refresh';

  currentUser = signal<User | null>(this.loadUser());

  constructor(private http: HttpClient, private router: Router) {}

  // ── Token helpers ──────────────────────────────────────────────────────────
  getAccessToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  // Alias used by LiveFeedComponent for SignalR connection
  getToken(): string | null {
    return this.getAccessToken();
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(this.REFRESH_KEY);
  }

  private saveSession(res: LoginResponse): void {
    localStorage.setItem(this.USER_KEY,    JSON.stringify(res.user));
    localStorage.setItem(this.TOKEN_KEY,   res.accessToken);
    localStorage.setItem(this.REFRESH_KEY, res.refreshToken);
    this.currentUser.set(res.user);
  }

  private clearSession(): void {
    localStorage.removeItem(this.USER_KEY);
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_KEY);
    this.currentUser.set(null);
  }

  private loadUser(): User | null {
    try {
      const stored = localStorage.getItem(this.USER_KEY);
      return stored ? JSON.parse(stored) : null;
    } catch { return null; }
  }

  // ── Auth API calls ─────────────────────────────────────────────────────────
  async login(credentials: LoginCredentials): Promise<boolean> {
    try {
      const res = await firstValueFrom(
        this.http.post<LoginResponse>(`${environment.apiUrl}/auth/login`, credentials)
      );
      this.saveSession(res);
      return true;
    } catch {
      return false;
    }
  }

  async refreshToken(): Promise<boolean> {
    const refreshToken = this.getRefreshToken();
    if (!refreshToken) return false;
    try {
      const res = await firstValueFrom(
        this.http.post<LoginResponse>(`${environment.apiUrl}/auth/refresh`, { refreshToken })
      );
      this.saveSession(res);
      return true;
    } catch {
      this.clearSession();
      return false;
    }
  }

  async logout(): Promise<void> {
    const refreshToken = this.getRefreshToken();
    if (refreshToken) {
      try {
        await firstValueFrom(
          this.http.post(`${environment.apiUrl}/auth/logout`, { refreshToken })
        );
      } catch { /* ignore errors on logout */ }
    }
    this.clearSession();
    this.router.navigate(['/login']);
  }

  // ── Role helpers ───────────────────────────────────────────────────────────
  isAuthenticated(): boolean  { return this.currentUser() !== null && !!this.getAccessToken(); }
  hasRole(role: string): boolean { return this.currentUser()?.role === role; }
  isAdmin(): boolean    { return this.currentUser()?.role === 'Admin'; }
  isOperator(): boolean { return this.currentUser()?.role === 'Operator'; }
  isViewer(): boolean   { return this.currentUser()?.role === 'Viewer'; }

  canEdit(): boolean {
    const role = this.currentUser()?.role;
    return role === 'Admin' || role === 'Operator';
  }

  canAccessConfig(): boolean {
    const role = this.currentUser()?.role;
    return role === 'Admin' || role === 'Operator';
  }

  canAccessAnalytics(): boolean {
    return this.currentUser()?.role === 'Admin';
  }
}
