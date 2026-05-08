import { Component, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { Camera, Incident, SystemStats } from '../../models';
import { LiveFeedComponent } from '../../shared/live-feed/live-feed.component';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, LiveFeedComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  stats         = signal<SystemStats>({ totalCameras: 0, onlineCameras: 0, todayIncidents: 0, activeAlerts: 0, systemUptime: '—', avgLatency: 0, detectionAccuracy: 0 });
  cameras       = signal<Camera[]>([]);
  recentIncidents = signal<Incident[]>([]);
  selectedCamera  = signal<Camera | null>(null);
  gridView        = signal<'2x2' | '3x2' | '1x1'>('2x2');
  loading         = signal(true);

  constructor(private api: ApiService) {}

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
  }

  selectCamera(cam: Camera): void  { this.selectedCamera.set(cam); }
  closeModal(): void               { this.selectedCamera.set(null); }

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
