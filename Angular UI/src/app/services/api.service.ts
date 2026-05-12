import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Camera, Incident, Alert, SystemStats, ManagedUser,
  CameraDetectionStat, MonthlyAlertStat, CameraAlertStat
} from '../models';

// ── Response shapes from API ──────────────────────────────────────────────────
export interface PagedResult<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface IncidentFilterParams {
  type?: string;
  severity?: string;
  status?: string;
  cameraId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface UserSessionSummary {
  userId: string;
  username: string;
  role: string;
  year: number;
  month: number;
  totalSessions: number;
  totalHoursActive: number;
}

export interface AnalyticsOverview {
  totalActiveHours: number;
  operatorHours: number;
  viewerHours: number;
  totalDetections: number;
  totalAlerts: number;
}

export interface AiSettings {
  fightDetection: boolean;
  weaponDetection: boolean;
  intrusionDetection: boolean;
  faceRecognition: boolean;
  licensePlate: boolean;
  globalConfidence: number;
  alertLatencyTarget: number;
  frameProcessingRate: number;
  gpuAcceleration: boolean;
  modelVersion: string;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // ── Dashboard ──────────────────────────────────────────────────────────────
  getStats(): Observable<SystemStats> {
    return this.http.get<SystemStats>(`${this.base}/dashboard/stats`);
  }

  // ── Cameras ────────────────────────────────────────────────────────────────
  getCameras(): Observable<Camera[]> {
    return this.http.get<Camera[]>(`${this.base}/cameras`);
  }

  createCamera(data: Partial<Camera>): Observable<Camera> {
    return this.http.post<Camera>(`${this.base}/cameras`, data);
  }

  updateCamera(id: string, data: Partial<Camera>): Observable<Camera> {
    return this.http.put<Camera>(`${this.base}/cameras/${id}`, data);
  }

  patchCameraDetection(id: string, detectionEnabled: boolean): Observable<Camera> {
    return this.http.patch<Camera>(`${this.base}/cameras/${id}/detection`, { detectionEnabled });
  }

  deleteCamera(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/cameras/${id}`);
  }

  // ── Camera Streaming ───────────────────────────────────────────────────────
  startCameraStream(id: string): Observable<{ cameraId: string; streaming: boolean; playlistUrl: string }> {
    return this.http.post<{ cameraId: string; streaming: boolean; playlistUrl: string }>(
      `${this.base}/cameras/${id}/stream/start`, {}
    );
  }

  stopCameraStream(id: string): Observable<{ cameraId: string; streaming: boolean }> {
    return this.http.post<{ cameraId: string; streaming: boolean }>(
      `${this.base}/cameras/${id}/stream/stop`, {}
    );
  }

  getCameraStreamStatus(id: string): Observable<{ cameraId: string; streaming: boolean; playlistUrl: string | null }> {
    return this.http.get<{ cameraId: string; streaming: boolean; playlistUrl: string | null }>(
      `${this.base}/cameras/${id}/stream/status`
    );
  }

  // ── Incidents ──────────────────────────────────────────────────────────────
  getIncidents(params: IncidentFilterParams = {}): Observable<PagedResult<Incident>> {
    let p = new HttpParams();
    if (params.type)     p = p.set('type',     params.type);
    if (params.severity) p = p.set('severity', params.severity);
    if (params.status)   p = p.set('status',   params.status);
    if (params.cameraId) p = p.set('cameraId', params.cameraId);
    if (params.search)   p = p.set('search',   params.search);
    p = p.set('page',     String(params.page     ?? 1));
    p = p.set('pageSize', String(params.pageSize ?? 200));
    return this.http.get<PagedResult<Incident>>(`${this.base}/incidents`, { params: p });
  }

  acknowledgeIncident(id: string): Observable<Incident> {
    return this.http.patch<Incident>(`${this.base}/incidents/${id}/acknowledge`, {});
  }

  resolveIncident(id: string, notes?: string): Observable<Incident> {
    return this.http.patch<Incident>(`${this.base}/incidents/${id}/resolve`, { notes });
  }

  // ── Alerts ─────────────────────────────────────────────────────────────────
  getAlerts(severity?: string, dismissed = false): Observable<Alert[]> {
    let p = new HttpParams().set('dismissed', String(dismissed));
    if (severity) p = p.set('severity', severity);
    return this.http.get<Alert[]>(`${this.base}/alerts`, { params: p });
  }

  markAlertRead(id: string): Observable<void> {
    return this.http.patch<void>(`${this.base}/alerts/${id}/read`, {});
  }

  markAllAlertsRead(): Observable<void> {
    return this.http.patch<void>(`${this.base}/alerts/read-all`, {});
  }

  dismissAlert(id: string): Observable<void> {
    return this.http.patch<void>(`${this.base}/alerts/${id}/dismiss`, {});
  }

  /** Escalate alert to emergency services — triggers ReceiveEmergencyNotification on all clients. */
  escalateAlert(id: string): Observable<any> {
    return this.http.patch<any>(`${this.base}/alerts/${id}/escalate`, {});
  }

  // ── Users ──────────────────────────────────────────────────────────────────
  getUsers(role?: string, status?: string, search?: string): Observable<ManagedUser[]> {
    let p = new HttpParams();
    if (role)   p = p.set('role',   role);
    if (status) p = p.set('status', status);
    if (search) p = p.set('search', search);
    return this.http.get<ManagedUser[]>(`${this.base}/users`, { params: p });
  }

  createUser(data: Partial<ManagedUser> & { password: string }): Observable<ManagedUser> {
    return this.http.post<ManagedUser>(`${this.base}/users`, data);
  }

  updateUser(id: string, data: Partial<ManagedUser> & { password?: string }): Observable<ManagedUser> {
    return this.http.put<ManagedUser>(`${this.base}/users/${id}`, data);
  }

  patchUserStatus(id: string, status: string): Observable<ManagedUser> {
    return this.http.patch<ManagedUser>(`${this.base}/users/${id}/status`, { status });
  }

  patchUserRole(id: string, role: string): Observable<ManagedUser> {
    return this.http.patch<ManagedUser>(`${this.base}/users/${id}/role`, { role });
  }

  deleteUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/users/${id}`);
  }

  // ── AI Settings ────────────────────────────────────────────────────────────
  getAiSettings(): Observable<AiSettings> {
    return this.http.get<AiSettings>(`${this.base}/settings/ai`);
  }

  updateAiSettings(data: AiSettings): Observable<AiSettings> {
    return this.http.put<AiSettings>(`${this.base}/settings/ai`, data);
  }

  // ── Analytics ──────────────────────────────────────────────────────────────
  getAnalyticsUserSessions(year: number, month?: number, role?: string): Observable<UserSessionSummary[]> {
    let p = new HttpParams().set('year', String(year));
    if (month && month > 0) p = p.set('month', String(month));
    if (role && role !== 'all') p = p.set('role', role);
    return this.http.get<UserSessionSummary[]>(`${this.base}/analytics/user-sessions`, { params: p });
  }

  getAnalyticsCameraDetections(): Observable<CameraDetectionStat[]> {
    return this.http.get<CameraDetectionStat[]>(`${this.base}/analytics/camera-detections`);
  }

  getAnalyticsMonthlyAlerts(year: number): Observable<MonthlyAlertStat[]> {
    return this.http.get<MonthlyAlertStat[]>(`${this.base}/analytics/monthly-alerts`, {
      params: new HttpParams().set('year', String(year))
    });
  }

  getAnalyticsCameraAlerts(): Observable<CameraAlertStat[]> {
    return this.http.get<CameraAlertStat[]>(`${this.base}/analytics/camera-alerts`);
  }

  getAnalyticsOverview(year: number): Observable<AnalyticsOverview> {
    return this.http.get<AnalyticsOverview>(`${this.base}/analytics/overview`, {
      params: new HttpParams().set('year', String(year))
    });
  }

  // ── System ─────────────────────────────────────────────────────────────────
  getSystemInfo(): Observable<any> {
    return this.http.get<any>(`${this.base}/system/info`);
  }

  // ── Face Recognition ───────────────────────────────────────────────────────
  /** List all registered faces from the AI service. */
  getFaces(): Observable<{ faces: string[]; total: number }> {
    return this.http.get<{ faces: string[]; total: number }>(`${this.base}/faces`);
  }

  /** Register a face photo for a username. */
  registerFace(username: string, photo: File): Observable<{ message: string; total_registered: number }> {
    const fd = new FormData();
    fd.append('username', username);
    fd.append('photo', photo, photo.name);
    return this.http.post<{ message: string; total_registered: number }>(
      `${this.base}/faces/register`, fd
    );
  }

  /** Delete a registered face by username. */
  deleteFace(username: string): Observable<{ message: string; total_registered: number }> {
    return this.http.delete<{ message: string; total_registered: number }>(
      `${this.base}/faces/${encodeURIComponent(username)}`
    );
  }
}
