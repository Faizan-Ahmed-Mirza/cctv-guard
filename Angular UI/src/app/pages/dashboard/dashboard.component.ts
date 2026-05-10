import { Component, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { Camera, Incident, SystemStats } from '../../models';
import { LiveFeedComponent } from '../../shared/live-feed/live-feed.component';
import { CameraStatusService } from '../../services/camera-status.service';
import { firstValueFrom, Subscription } from 'rxjs';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, TitleCasePipe, LiveFeedComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit, OnDestroy {
  stats           = signal<SystemStats>({ totalCameras: 0, onlineCameras: 0, todayIncidents: 0, activeAlerts: 0, systemUptime: '—', avgLatency: 0, detectionAccuracy: 0 });
  cameras         = signal<Camera[]>([]);
  recentIncidents = signal<Incident[]>([]);
  selectedCamera  = signal<Camera | null>(null);
  gridView        = signal<'2x2' | '3x2' | '1x1'>('2x2');
  loading         = signal(true);

  // Tracks which cameras are currently toggling (to show spinner on btn)
  togglingStream  = signal<Set<string>>(new Set());

  private statusSub?: Subscription;

  constructor(private api: ApiService, private cameraStatus: CameraStatusService) {}

  async ngOnInit(): Promise<void> {
    try {
      const [statsRes, camerasRes, incidentsRes] = await Promise.all([
        firstValueFrom(this.api.getStats()),
        firstValueFrom(this.api.getCameras()),
        firstValueFrom(this.api.getIncidents({ pageSize: 5 })),
      ]);
      this.stats.set(statsRes);
      this.cameras.set(camerasRes);
      this.recentIncidents.set(incidentsRes.data);
    } finally {
      this.loading.set(false);
    }

    // Connect to SignalR and listen for real-time camera status changes
    await this.cameraStatus.connect();
    this.statusSub = this.cameraStatus.statusChange$.subscribe(event => {
      this.cameras.update(list =>
        list.map(c => c.id === event.id ? { ...c, status: event.status } : c)
      );
      // Also update the selected camera modal if it's open
      const sel = this.selectedCamera();
      if (sel?.id === event.id) {
        this.selectedCamera.set({ ...sel, status: event.status });
      }
      // Refresh stats count
      const all = this.cameras();
      this.stats.update(s => ({ ...s, onlineCameras: all.filter(c => c.status === 'online').length }));
    });
  }

  ngOnDestroy(): void {
    this.statusSub?.unsubscribe();
  }

  selectCamera(cam: Camera): void { this.selectedCamera.set(cam); }
  closeModal(): void              { this.selectedCamera.set(null); }

  isStreamToggling(camId: string): boolean {
    return this.togglingStream().has(camId);
  }

  async toggleStream(cam: Camera, event: Event): Promise<void> {
    event.stopPropagation();
    if (this.isStreamToggling(cam.id)) return;

    this.togglingStream.update(s => new Set(s).add(cam.id));
    try {
      if (cam.status === 'online') {
        // User manually turning OFF — mark as disabled so health check won't flip it back
        this.cameraStatus.setManuallyDisabled(cam.id, true);
        await firstValueFrom(this.api.stopCameraStream(cam.id));
        this.cameras.update(list =>
          list.map(c => c.id === cam.id ? { ...c, status: 'offline' as const } : c)
        );
      } else {
        // User manually turning ON — clear the disabled flag
        this.cameraStatus.setManuallyDisabled(cam.id, false);
        await firstValueFrom(this.api.startCameraStream(cam.id));
        this.cameras.update(list =>
          list.map(c => c.id === cam.id ? { ...c, status: 'online' as const } : c)
        );
        this.cameraStatus.emitStatus(cam.id, 'online');
      }
    } catch {
      // Revert the disabled flag on error
      this.cameraStatus.setManuallyDisabled(cam.id, false);
    } finally {
      this.togglingStream.update(s => { const n = new Set(s); n.delete(cam.id); return n; });
    }
  }

  getStatusClass(status: string): string {
    return ({ online: 'success', offline: 'secondary', error: 'danger' } as Record<string,string>)[status] ?? 'secondary';
  }

  getSeverityClass(severity: string): string {
    return ({ critical: 'danger', high: 'warning', medium: 'info', low: 'secondary' } as Record<string,string>)[severity] ?? 'secondary';
  }

  getIncidentIcon(type: string): string {
    return ({ fight: '🥊', weapon: '🔫', intrusion: '🚨', unknown_face: '👤', license_plate: '🚗' } as Record<string,string>)[type] ?? '⚠️';
  }

  formatTime(date: Date): string {
    const d    = new Date(date);
    const diff = Date.now() - d.getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1)  return 'Just now';
    if (mins < 60) return `${mins}m ago`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24)  return `${hrs}h ago`;
    return d.toLocaleDateString();
  }
}
