import { Component, signal, computed, OnInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, UserSessionSummary, AnalyticsOverview } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { CameraDetectionStat, MonthlyAlertStat, CameraAlertStat } from '../../models';
import { firstValueFrom } from 'rxjs';
import * as XLSX from 'xlsx';

type ActiveTab = 'overview' | 'users' | 'cameras' | 'alerts';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss'
})
export class AnalyticsComponent implements OnInit {
  activeTab     = signal<ActiveTab>('overview');
  selectedYear  = signal<number>(2025);
  selectedMonth = signal<number>(0);
  selectedRole  = signal<string>('all');
  loading       = signal(true);
  exporting     = signal(false);

  readonly years  = [2024, 2025];
  readonly months = [
    { value: 0, label: 'All Months' },
    { value: 1, label: 'January' }, { value: 2, label: 'February' },
    { value: 3, label: 'March' },   { value: 4, label: 'April' },
    { value: 5, label: 'May' },     { value: 6, label: 'June' },
    { value: 7, label: 'July' },    { value: 8, label: 'August' },
    { value: 9, label: 'September' }, { value: 10, label: 'October' },
    { value: 11, label: 'November' }, { value: 12, label: 'December' },
  ];
  readonly monthLabels = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

  // ── Raw data signals ───────────────────────────────────────────────────────
  private userSessions      = signal<UserSessionSummary[]>([]);
  private cameraDetections  = signal<CameraDetectionStat[]>([]);
  private monthlyAlerts     = signal<MonthlyAlertStat[]>([]);
  private cameraAlerts      = signal<CameraAlertStat[]>([]);
  private overview          = signal<AnalyticsOverview | null>(null);

  // ── Computed ───────────────────────────────────────────────────────────────
  filteredSessions = computed(() => {
    let s = this.userSessions();
    if (this.selectedMonth() > 0) s = s.filter(x => x.month === this.selectedMonth());
    if (this.selectedRole() !== 'all') s = s.filter(x => x.role === this.selectedRole());
    return s;
  });

  userHoursSummary = computed(() => {
    const map = new Map<string, { username: string; role: string; totalHours: number; sessions: number }>();
    for (const s of this.filteredSessions()) {
      const ex = map.get(s.userId);
      if (ex) { ex.totalHours += s.totalHoursActive; ex.sessions += s.totalSessions; }
      else map.set(s.userId, { username: s.username, role: s.role, totalHours: s.totalHoursActive, sessions: s.totalSessions });
    }
    return Array.from(map.values()).sort((a, b) => b.totalHours - a.totalHours);
  });

  userMonthlyHours = computed(() => {
    const users = [...new Set(this.userSessions().map(s => s.username))];
    return users.map(username => {
      const monthlyData = this.monthLabels.map((_, i) => {
        const month = i + 1;
        return this.userSessions()
          .filter(s => s.username === username && s.month === month)
          .reduce((sum, s) => sum + s.totalHoursActive, 0);
      });
      const role = this.userSessions().find(s => s.username === username)?.role ?? '';
      return { username, role, monthlyData };
    });
  });

  cameraDetectionStats = computed(() => this.cameraDetections());
  topCamera = computed(() => [...this.cameraDetections()].sort((a, b) => b.totalDetections - a.totalDetections)[0]);

  monthlyAlertStats = computed(() => {
    let stats = this.monthlyAlerts();
    if (this.selectedMonth() > 0) stats = stats.filter(s => s.month === this.selectedMonth());
    return stats;
  });

  totalAlertsFiltered = computed(() => this.monthlyAlertStats().reduce((sum, s) => sum + s.total, 0));
  cameraAlertStats    = computed(() => [...this.cameraAlerts()].sort((a, b) => b.total - a.total));

  overviewKpis = computed(() => {
    const ov = this.overview();
    return {
      totalHours:      ov?.totalActiveHours  ?? 0,
      operatorHours:   ov?.operatorHours     ?? 0,
      viewerHours:     ov?.viewerHours       ?? 0,
      totalDetections: ov?.totalDetections   ?? 0,
      totalAlerts:     ov?.totalAlerts       ?? 0,
    };
  });

  constructor(private api: ApiService, public auth: AuthService) {}

  async ngOnInit(): Promise<void> {
    await this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    try {
      const year = this.selectedYear();
      const [sessions, detections, monthly, camAlerts, ov] = await Promise.all([
        firstValueFrom(this.api.getAnalyticsUserSessions(year)),
        firstValueFrom(this.api.getAnalyticsCameraDetections()),
        firstValueFrom(this.api.getAnalyticsMonthlyAlerts(year)),
        firstValueFrom(this.api.getAnalyticsCameraAlerts()),
        firstValueFrom(this.api.getAnalyticsOverview(year)),
      ]);
      this.userSessions.set(sessions);
      this.cameraDetections.set(detections);
      this.monthlyAlerts.set(monthly);
      this.cameraAlerts.set(camAlerts);
      this.overview.set(ov);
    } finally {
      this.loading.set(false);
    }
  }

  async onYearChange(year: number): Promise<void> {
    this.selectedYear.set(year);
    await this.loadAll();
  }

  setTab(tab: ActiveTab): void { this.activeTab.set(tab); }

  // ── Chart helpers ──────────────────────────────────────────────────────────
  getRoleClass(role: string): string {
    return ({ Admin: 'info', Operator: 'warning', Viewer: 'secondary' } as Record<string,string>)[role] ?? 'secondary';
  }
  getRoleIcon(role: string): string {
    return ({ Admin: '🛡️', Operator: '👁️', Viewer: '👤' } as Record<string,string>)[role] ?? '👤';
  }
  getBarWidth(value: number, max: number): number { return max > 0 ? Math.round((value / max) * 100) : 0; }
  getMaxDetections(): number    { return Math.max(...this.cameraDetectionStats().map(c => c.totalDetections), 1); }
  getMaxCameraAlerts(): number  { return Math.max(...this.cameraAlertStats().map(c => c.total), 1); }
  getMaxMonthlyAlerts(): number { return Math.max(...this.monthlyAlertStats().map(s => s.total), 1); }
  getMaxUserHours(): number     { return Math.max(...this.userHoursSummary().map(u => u.totalHours), 1); }
  getMaxMonthlyUserHours(): number {
    const all = this.userMonthlyHours().flatMap(u => u.monthlyData);
    return Math.max(...all, 1);
  }

  getDetectionTypeBreakdown(stat: CameraDetectionStat): { label: string; count: number; color: string }[] {
    return [
      { label: 'Fight',         count: stat.fightCount,        color: 'var(--accent-red)'    },
      { label: 'Weapon',        count: stat.weaponCount,       color: 'var(--accent-orange)' },
      { label: 'Intrusion',     count: stat.intrusionCount,    color: 'var(--accent-yellow)' },
      { label: 'Unknown Face',  count: stat.unknownFaceCount,  color: 'var(--accent-blue)'   },
      { label: 'License Plate', count: stat.licensePlateCount, color: 'var(--accent-green)'  },
    ].filter(d => d.count > 0);
  }

  getAlertSeverityBreakdown(stat: MonthlyAlertStat): { label: string; count: number; color: string }[] {
    return [
      { label: 'Critical', count: stat.critical, color: 'var(--accent-red)'    },
      { label: 'High',     count: stat.high,     color: 'var(--accent-orange)' },
      { label: 'Medium',   count: stat.medium,   color: 'var(--accent-yellow)' },
      { label: 'Low',      count: stat.low,      color: 'var(--accent-blue)'   },
    ];
  }

  getUserMonthlyColor(role: string): string {
    return ({ Admin: '#3b82f6', Operator: '#f59e0b', Viewer: '#10b981' } as Record<string,string>)[role] ?? '#3b82f6';
  }

  getMonthlyAlertBarSegments(stat: MonthlyAlertStat): { color: string; pct: number }[] {
    const total = stat.total || 1;
    return [
      { color: 'var(--accent-red)',    pct: (stat.critical / total) * 100 },
      { color: 'var(--accent-orange)', pct: (stat.high     / total) * 100 },
      { color: 'var(--accent-yellow)', pct: (stat.medium   / total) * 100 },
      { color: 'var(--accent-blue)',   pct: (stat.low      / total) * 100 },
    ];
  }

  // ── Excel export ───────────────────────────────────────────────────────────
  exportToExcel(): void {
    this.exporting.set(true);
    const date = new Date().toISOString().slice(0, 10);
    const wb   = XLSX.utils.book_new();

    const ws1 = XLSX.utils.json_to_sheet(this.userHoursSummary().map(u => ({
      'Username': u.username, 'Role': u.role, 'Sessions': u.sessions,
      'Total Hours': u.totalHours, 'Avg Hours/Session': parseFloat((u.totalHours / u.sessions).toFixed(1)),
      'Year': this.selectedYear(), 'Month Filter': this.selectedMonth() === 0 ? 'All Months' : this.months[this.selectedMonth()].label,
    })));
    ws1['!cols'] = [{ wch: 18 }, { wch: 12 }, { wch: 10 }, { wch: 14 }, { wch: 20 }, { wch: 8 }, { wch: 16 }];
    XLSX.utils.book_append_sheet(wb, ws1, 'User Active Hours');

    const ws2 = XLSX.utils.json_to_sheet(this.cameraDetectionStats().map(c => ({
      'Camera Name': c.cameraName, 'Camera ID': c.cameraId, 'Total Detections': c.totalDetections,
      'Fights': c.fightCount, 'Weapons': c.weaponCount, 'Intrusions': c.intrusionCount,
      'Unknown Faces': c.unknownFaceCount, 'License Plates': c.licensePlateCount,
    })));
    ws2['!cols'] = [{ wch: 22 }, { wch: 10 }, { wch: 18 }, { wch: 8 }, { wch: 10 }, { wch: 12 }, { wch: 16 }, { wch: 16 }];
    XLSX.utils.book_append_sheet(wb, ws2, 'Camera Detections');

    const ws3 = XLSX.utils.json_to_sheet(this.monthlyAlerts().map(s => ({
      'Month': s.label, 'Year': s.year, 'Total': s.total,
      'Critical': s.critical, 'High': s.high, 'Medium': s.medium, 'Low': s.low,
    })));
    ws3['!cols'] = [{ wch: 14 }, { wch: 8 }, { wch: 8 }, { wch: 10 }, { wch: 8 }, { wch: 10 }, { wch: 8 }];
    XLSX.utils.book_append_sheet(wb, ws3, 'Monthly Alert Trends');

    const ws4 = XLSX.utils.json_to_sheet(this.cameraAlertStats().map(c => ({
      'Camera Name': c.cameraName, 'Camera ID': c.cameraId,
      'Total Alerts': c.total, 'Critical': c.critical, 'High': c.high,
    })));
    ws4['!cols'] = [{ wch: 22 }, { wch: 10 }, { wch: 14 }, { wch: 10 }, { wch: 8 }];
    XLSX.utils.book_append_sheet(wb, ws4, 'Camera Alert Stats');

    XLSX.writeFile(wb, `analytics-report-${this.selectedYear()}-${date}.xlsx`);
    this.exporting.set(false);
  }
}
