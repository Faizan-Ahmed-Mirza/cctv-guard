import { Component, signal, computed, OnInit, OnDestroy, AfterViewChecked, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { Camera, Incident } from '../../models';
import { firstValueFrom } from 'rxjs';
import * as XLSX from 'xlsx';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-incidents',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './incidents.component.html',
  styleUrl: './incidents.component.scss'
})
export class IncidentsComponent implements OnInit, OnDestroy, AfterViewChecked {
  filterType     = signal('all');
  filterSeverity = signal('all');
  filterStatus   = signal('all');
  filterCamera   = signal('all');
  searchQuery    = signal('');
  selectedIncident = signal<Incident | null>(null);
  exporting      = signal(false);
  loading        = signal(true);
  escalating     = signal(false); // spinner on emergency button in modal

  cameras   = signal<Camera[]>([]);
  incidents = signal<Incident[]>([]);

  @ViewChild('thumbCanvas') thumbCanvasRef?: ElementRef<HTMLCanvasElement>;
  private lastRenderedIncidentId: string | null = null;
  private hubConnection: signalR.HubConnection | null = null;

  filteredIncidents = computed(() => {
    let list = this.incidents();
    if (this.filterType()     !== 'all') list = list.filter(i => i.type     === this.filterType());
    if (this.filterSeverity() !== 'all') list = list.filter(i => i.severity === this.filterSeverity());
    if (this.filterStatus()   !== 'all') list = list.filter(i => i.status   === this.filterStatus());
    if (this.filterCamera()   !== 'all') list = list.filter(i => i.cameraId === this.filterCamera());
    if (this.searchQuery()) {
      const q = this.searchQuery().toLowerCase();
      list = list.filter(i =>
        i.type.includes(q) || i.cameraName.toLowerCase().includes(q) || i.id.includes(q)
      );
    }
    return list.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
  });

  constructor(private api: ApiService, public auth: AuthService) {}

  async ngOnInit(): Promise<void> {
    try {
      const [camerasRes, incidentsRes] = await Promise.all([
        firstValueFrom(this.api.getCameras()),
        firstValueFrom(this.api.getIncidents({ pageSize: 200 })),
      ]);
      this.cameras.set(camerasRes);
      this.incidents.set(incidentsRes.data);
    } finally {
      this.loading.set(false);
    }
    this.connectSignalR();
  }

  ngOnDestroy(): void {
    this.hubConnection?.stop().catch(() => {});
  }

  private connectSignalR(): void {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.getAccessToken() ?? '',
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Error)
      .build();

    // Real-time incident status updates — from any client (web or mobile)
    this.hubConnection.on('IncidentUpdated', (data: { id: string; status: string }) => {
      this.incidents.update(list =>
        list.map(i => i.id === data.id ? { ...i, status: data.status as any } : i)
      );
      // Also update the open modal if it's showing this incident
      const sel = this.selectedIncident();
      if (sel?.id === data.id) {
        this.selectedIncident.set({ ...sel, status: data.status as any });
      }
    });

    this.hubConnection.start().catch(() => {});
  }

  ngAfterViewChecked(): void {
    const inc = this.selectedIncident();
    if (!inc || !inc.thumbnailUrl) return;
    if (inc.id === this.lastRenderedIncidentId) return; // already rendered
    const canvas = this.thumbCanvasRef?.nativeElement;
    if (!canvas) return;
    this.lastRenderedIncidentId = inc.id;
    this.renderThumbnail(canvas, inc);
  }

  /**
   * Renders the incident thumbnail onto a canvas with the bounding box correctly positioned.
   * Uses the same letterbox-aware math as the live feed component.
   */
  private renderThumbnail(canvas: HTMLCanvasElement, inc: Incident): void {
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const img = new Image();
    img.onload = () => {
      // Set canvas to the container width, maintain aspect ratio
      const containerW = canvas.parentElement?.clientWidth || 560;
      const aspect     = img.naturalWidth / img.naturalHeight;
      const canvasW    = containerW;
      const canvasH    = Math.min(containerW / aspect, 320); // max 320px tall

      canvas.width  = canvasW;
      canvas.height = canvasH;

      // Draw image scaled to fit (object-fit: contain equivalent)
      const imgAspect  = img.naturalWidth / img.naturalHeight;
      const canvAspect = canvasW / canvasH;

      let renderW: number, renderH: number, offsetX: number, offsetY: number;
      if (imgAspect > canvAspect) {
        renderW = canvasW;
        renderH = canvasW / imgAspect;
        offsetX = 0;
        offsetY = (canvasH - renderH) / 2;
      } else {
        renderH = canvasH;
        renderW = canvasH * imgAspect;
        offsetX = (canvasW - renderW) / 2;
        offsetY = 0;
      }

      // Fill background black (letterbox bars)
      ctx.fillStyle = '#000';
      ctx.fillRect(0, 0, canvasW, canvasH);

      // Draw the image
      ctx.drawImage(img, offsetX, offsetY, renderW, renderH);

      // Draw bounding box if available
      if (inc.boundingBox) {
        const scaleX = renderW / img.naturalWidth;
        const scaleY = renderH / img.naturalHeight;

        const bx = offsetX + inc.boundingBox.x * scaleX;
        const by = offsetY + inc.boundingBox.y * scaleY;
        const bw = inc.boundingBox.width  * scaleX;
        const bh = inc.boundingBox.height * scaleY;

        // Box
        ctx.strokeStyle = '#ef4444';
        ctx.lineWidth   = 2.5;
        ctx.strokeRect(bx, by, bw, bh);
        ctx.fillStyle = 'rgba(239,68,68,0.15)';
        ctx.fillRect(bx, by, bw, bh);

        // Label badge
        const label = `${inc.type.replace(/_/g, ' ')} ${(inc.confidence * 100).toFixed(0)}%`;
        ctx.font = 'bold 13px sans-serif';
        const tw = ctx.measureText(label).width + 10;
        const th = 20;
        const ly = by > th ? by - th : by + bh;
        ctx.fillStyle = '#ef4444';
        ctx.fillRect(bx, ly, tw, th);
        ctx.fillStyle = '#fff';
        ctx.fillText(label, bx + 5, ly + 14);
      }

      // Timestamp overlay
      const ts = new Date(inc.timestamp).toLocaleString();
      ctx.font = '10px monospace';
      const tsW = ctx.measureText(ts).width + 10;
      ctx.fillStyle = 'rgba(0,0,0,0.6)';
      ctx.fillRect(8, canvasH - 22, tsW, 18);
      ctx.fillStyle = 'rgba(255,255,255,0.8)';
      ctx.fillText(ts, 13, canvasH - 8);
    };
    img.src = inc.thumbnailUrl!;
  }

  async acknowledge(id: string): Promise<void> {
    const updated = await firstValueFrom(this.api.acknowledgeIncident(id));
    this.incidents.update(list => list.map(i => i.id === id ? updated : i));
    if (this.selectedIncident()?.id === id) this.selectedIncident.set(updated);
  }

  async resolve(id: string): Promise<void> {
    const updated = await firstValueFrom(this.api.resolveIncident(id));
    this.incidents.update(list => list.map(i => i.id === id ? updated : i));
    if (this.selectedIncident()?.id === id) this.selectedIncident.set(updated);
  }

  /** Escalate the incident's linked alert to emergency services. */
  async escalateIncident(inc: Incident): Promise<void> {
    if (this.escalating()) return;
    this.escalating.set(true);
    try {
      // The alert ID matches the incident ID pattern — find the alert via incidentId
      // We call the alerts escalate endpoint using the incident's linked alert
      await firstValueFrom(this.api.escalateAlertByIncident(inc.id));
    } catch (err) {
      console.error('[EMERGENCY] Escalation failed:', err);
    } finally {
      this.escalating.set(false);
    }
  }

  viewDetails(inc: Incident): void {
    this.lastRenderedIncidentId = null; // force re-render for new incident
    this.selectedIncident.set(inc);
  }
  closeModal(): void {
    this.selectedIncident.set(null);
    this.lastRenderedIncidentId = null;
  }

  // thumbScale kept for any legacy references — no longer used for positioning
  readonly thumbScale = 0.75;

  exportToExcel(): void {
    const rows = this.filteredIncidents();
    if (rows.length === 0) return;
    this.exporting.set(true);

    const data = rows.map(inc => ({
      'Incident ID':    inc.id,
      'Type':           inc.type.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase()),
      'Camera Name':    inc.cameraName,
      'Camera ID':      inc.cameraId,
      'Severity':       inc.severity.charAt(0).toUpperCase() + inc.severity.slice(1),
      'Confidence (%)': parseFloat((inc.confidence * 100).toFixed(1)),
      'Timestamp':      new Date(inc.timestamp).toLocaleString(),
      'Status':         inc.status.charAt(0).toUpperCase() + inc.status.slice(1),
      'Notes':          inc.notes ?? '',
    }));

    const ws = XLSX.utils.json_to_sheet(data);
    ws['!cols'] = [{ wch: 14 }, { wch: 18 }, { wch: 20 }, { wch: 12 }, { wch: 12 }, { wch: 16 }, { wch: 22 }, { wch: 14 }, { wch: 40 }];
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Incident Log');
    XLSX.writeFile(wb, `incident-log-${new Date().toISOString().slice(0, 10)}.xlsx`);
    this.exporting.set(false);
  }

  getSeverityClass(s: string): string {
    return ({ critical: 'danger', high: 'warning', medium: 'info', low: 'secondary' } as Record<string,string>)[s] ?? 'secondary';
  }
  getStatusClass(s: string): string {
    return ({ new: 'danger', acknowledged: 'warning', resolved: 'success' } as Record<string,string>)[s] ?? 'secondary';
  }
  getIncidentIcon(type: string): string {
    return ({
      fight: '🥊', weapon: '🔫', intrusion: '🚨',
      unknown_face: '👤', license_plate: '🚗', fire: '🔥'
    } as Record<string,string>)[type] ?? '⚠️';
  }
  formatType(type: string): string {
    return type.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }
  formatDate(date: Date): string { return new Date(date).toLocaleString(); }
}
