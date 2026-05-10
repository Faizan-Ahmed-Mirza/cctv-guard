import { Component, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { Alert } from '../../models';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-alerts',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './alerts.component.html',
  styleUrl: './alerts.component.scss'
})
export class AlertsComponent implements OnInit {
  filterSeverity     = signal('all');
  showDismissed      = signal(false);
  loading            = signal(true);
  emergencyAlert     = signal<Alert | null>(null);   // alert pending emergency confirmation
  emergencyDispatched = signal(false);               // shows the success toast

  private allAlerts = signal<Alert[]>([]);
  private dismissed = signal<Alert[]>([]);

  activeAlerts = computed(() => {
    let list = this.allAlerts();
    if (this.filterSeverity() !== 'all')
      list = list.filter(a => a.severity === this.filterSeverity());
    return list.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
  });

  dismissedAlerts = computed(() =>
    this.dismissed().sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime())
  );

  unreadCount = computed(() => this.allAlerts().filter(a => !a.read).length);

  currentTime = new Date();

  constructor(private api: ApiService, public auth: AuthService) {
    setInterval(() => this.currentTime = new Date(), 1000);
  }

  async ngOnInit(): Promise<void> {
    await this.loadAlerts();
  }

  private async loadAlerts(): Promise<void> {
    try {
      const [active, dismissed] = await Promise.all([
        firstValueFrom(this.api.getAlerts(undefined, false)),
        firstValueFrom(this.api.getAlerts(undefined, true)),
      ]);
      this.allAlerts.set(active);
      this.dismissed.set(dismissed);
    } finally {
      this.loading.set(false);
    }
  }

  toggleDismissed(): void { this.showDismissed.update(v => !v); }

  async dismiss(id: string): Promise<void> {
    await firstValueFrom(this.api.dismissAlert(id));
    const alert = this.allAlerts().find(a => a.id === id);
    if (alert) {
      this.allAlerts.update(list => list.filter(a => a.id !== id));
      this.dismissed.update(list => [{ ...alert, dismissed: true }, ...list]);
    }
  }

  async markRead(id: string): Promise<void> {
    await firstValueFrom(this.api.markAlertRead(id));
    this.allAlerts.update(list => list.map(a => a.id === id ? { ...a, read: true } : a));
  }

  async markAllRead(): Promise<void> {
    await firstValueFrom(this.api.markAllAlertsRead());
    this.allAlerts.update(list => list.map(a => ({ ...a, read: true })));
  }

  // ── Emergency button ───────────────────────────────────────────────────────

  /** Opens the confirmation modal for the given alert. */
  contactEmergency(alert: Alert): void {
    this.emergencyAlert.set(alert);
  }

  /** User cancelled — close the modal. */
  cancelEmergency(): void {
    this.emergencyAlert.set(null);
  }

  /** User confirmed — log the action, show success toast, auto-dismiss after 5s. */
  confirmEmergency(): void {
    const alert = this.emergencyAlert();
    if (!alert) return;

    // Log to console for audit trail (in production this would call an API endpoint)
    console.warn('[EMERGENCY] Services contacted for alert:', {
      id:       alert.id,
      type:     alert.type,
      severity: alert.severity,
      camera:   alert.cameraName,
      time:     new Date(alert.timestamp).toLocaleString(),
    });

    this.emergencyAlert.set(null);
    this.emergencyDispatched.set(true);

    // Auto-hide the toast after 5 seconds
    setTimeout(() => this.emergencyDispatched.set(false), 5000);
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  getSeverityClass(s: string): string {
    return ({ critical: 'danger', high: 'warning', medium: 'info', low: 'secondary' } as Record<string,string>)[s] ?? 'secondary';
  }
  getSeverityIcon(s: string): string {
    return ({ critical: '🔴', high: '🟠', medium: '🟡', low: '🔵' } as Record<string,string>)[s] ?? '⚪';
  }
  formatTime(date: Date): string {
    const d    = new Date(date);
    const diff = Date.now() - d.getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1)  return 'Just now';
    if (mins < 60) return `${mins} min ago`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24)  return `${hrs}h ago`;
    return d.toLocaleString();
  }
}
