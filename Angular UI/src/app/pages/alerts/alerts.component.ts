import { Component, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { AlertBadgeService } from '../../services/alert-badge.service';
import { Alert, EmergencyNotification } from '../../models';
import { firstValueFrom } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-alerts',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './alerts.component.html',
  styleUrl: './alerts.component.scss'
})
export class AlertsComponent implements OnInit, OnDestroy {
  filterSeverity      = signal('all');
  showDismissed       = signal(false);
  loading             = signal(true);
  escalating          = signal(false);              // spinner on confirm button
  emergencyAlert      = signal<Alert | null>(null); // alert pending confirmation
  emergencyDispatched = signal(false);              // success toast

  private allAlerts = signal<Alert[]>([]);
  private dismissed = signal<Alert[]>([]);

  private hubConnection: signalR.HubConnection | null = null;

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

  constructor(private api: ApiService, public auth: AuthService, private badge: AlertBadgeService) {
    setInterval(() => this.currentTime = new Date(), 1000);
  }

  async ngOnInit(): Promise<void> {
    await this.loadAlerts();
    this.badge.reset();
    try { await firstValueFrom(this.api.markAllAlertsRead()); } catch { /* ignore */ }
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

    // When any operator escalates an alert, mark it in our local list
    this.hubConnection.on('ReceiveEmergencyNotification', (data: EmergencyNotification) => {
      this.allAlerts.update(list =>
        list.map(a => a.id === data.alertId ? { ...a, isEscalated: true } : a)
      );
    });

    // Sync dismissals from other sessions
    this.hubConnection.on('AlertDismissed', (data: { alertId: string }) => {
      const alert = this.allAlerts().find(a => a.id === data.alertId);
      if (alert) {
        this.allAlerts.update(list => list.filter(a => a.id !== data.alertId));
        this.dismissed.update(list => [{ ...alert, dismissed: true }, ...list]);
      }
    });

    this.hubConnection.start().catch(() => { /* silent — page still works without real-time */ });
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
    this.badge.set(this.allAlerts().filter(a => !a.read).length);
  }

  async markAllRead(): Promise<void> {
    await firstValueFrom(this.api.markAllAlertsRead());
    this.allAlerts.update(list => list.map(a => ({ ...a, read: true })));
    this.badge.reset();
  }

  // ── Emergency ──────────────────────────────────────────────────────────────

  contactEmergency(alert: Alert): void {
    this.emergencyAlert.set(alert);
  }

  cancelEmergency(): void {
    if (this.escalating()) return; // don't close while request is in-flight
    this.emergencyAlert.set(null);
  }

  async confirmEmergency(): Promise<void> {
    const alert = this.emergencyAlert();
    if (!alert || this.escalating()) return;

    this.escalating.set(true);
    try {
      // POST to backend → saves escalation + broadcasts ReceiveEmergencyNotification
      // to ALL SignalR clients (Angular tabs + Flutter mobile app)
      await firstValueFrom(this.api.escalateAlert(alert.id));

      // Mark locally as escalated so the badge shows immediately
      this.allAlerts.update(list =>
        list.map(a => a.id === alert.id ? { ...a, isEscalated: true } : a)
      );
    } catch (err) {
      console.error('[EMERGENCY] Escalation API call failed:', err);
      // Still proceed — operator action must not be blocked by network errors
    } finally {
      this.escalating.set(false);
      this.emergencyAlert.set(null);
      this.emergencyDispatched.set(true);
      setTimeout(() => this.emergencyDispatched.set(false), 5000);
    }
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
