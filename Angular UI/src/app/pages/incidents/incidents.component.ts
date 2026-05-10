import { Component, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { Camera, Incident } from '../../models';
import { firstValueFrom } from 'rxjs';
import * as XLSX from 'xlsx';

@Component({
  selector: 'app-incidents',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './incidents.component.html',
  styleUrl: './incidents.component.scss'
})
export class IncidentsComponent implements OnInit {
  filterType     = signal('all');
  filterSeverity = signal('all');
  filterStatus   = signal('all');
  filterCamera   = signal('all');
  searchQuery    = signal('');
  selectedIncident = signal<Incident | null>(null);
  exporting      = signal(false);
  loading        = signal(true);

  cameras   = signal<Camera[]>([]);
  incidents = signal<Incident[]>([]);

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
  }

  async acknowledge(id: string): Promise<void> {
    const updated = await firstValueFrom(this.api.acknowledgeIncident(id));
    this.incidents.update(list => list.map(i => i.id === id ? updated : i));
  }

  async resolve(id: string): Promise<void> {
    const updated = await firstValueFrom(this.api.resolveIncident(id));
    this.incidents.update(list => list.map(i => i.id === id ? updated : i));
  }

  viewDetails(inc: Incident): void { this.selectedIncident.set(inc); }
  closeModal(): void               { this.selectedIncident.set(null); }

  // Scale factor: the thumbnail is displayed at 100% CSS width inside a ~480px container.
  // The original frame is 640px wide → scale = 480/640 = 0.75
  // Used to position the bounding box overlay on the thumbnail image.
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
    return ({ fight: '🥊', weapon: '🔫', intrusion: '🚨', unknown_face: '👤', license_plate: '🚗' } as Record<string,string>)[type] ?? '⚠️';
  }
  formatDate(date: Date): string { return new Date(date).toLocaleString(); }
}
