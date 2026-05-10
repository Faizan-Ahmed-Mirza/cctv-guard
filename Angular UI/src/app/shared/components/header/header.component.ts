import { Component, signal, OnInit, OnDestroy, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { ApiService } from '../../../services/api.service';
import { ThemeService } from '../../../services/theme.service';
import { CameraStatusService } from '../../../services/camera-status.service';
import { AlertBadgeService } from '../../../services/alert-badge.service';
import { firstValueFrom, Subscription } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../../environments/environment';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent implements OnInit, OnDestroy {
  @Output() toggleSidebar = new EventEmitter<void>();

  currentTime = new Date();

  private clockInterval: ReturnType<typeof setInterval> | null = null;
  private pollInterval:  ReturnType<typeof setInterval> | null = null;
  private alertConnection: signalR.HubConnection | null = null;
  private badgeSub: Subscription | null = null;

  // Expose badge count as a signal for the template
  unreadAlerts = signal(0);

  constructor(
    public auth: AuthService,
    public theme: ThemeService,
    private api: ApiService,
    private cameraStatus: CameraStatusService,
    private badge: AlertBadgeService
  ) {}

  async ngOnInit(): Promise<void> {
    this.clockInterval = setInterval(() => this.currentTime = new Date(), 1000);

    // Sync badge service → local signal for template rendering
    this.badgeSub = this.badge.count$.subscribe(n => this.unreadAlerts.set(n));

    // Initial load from API
    await this.loadUnreadCount();

    // Poll every 30s as fallback (catches any missed SignalR events)
    this.pollInterval = setInterval(() => this.loadUnreadCount(), 30000);

    // Real-time: listen for NewAlert and AlertDismissed on the alerts hub
    await this.cameraStatus.connect();
    this.subscribeToNewAlerts();
  }

  ngOnDestroy(): void {
    if (this.clockInterval) clearInterval(this.clockInterval);
    if (this.pollInterval)  clearInterval(this.pollInterval);
    this.badgeSub?.unsubscribe();
    if (this.alertConnection) {
      this.alertConnection.stop().catch(() => {});
      this.alertConnection = null;
    }
  }

  private subscribeToNewAlerts(): void {
    if (!this.auth.getAccessToken()) return;

    this.alertConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.getAccessToken() ?? '',
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect([0, 2000, 5000])
      .configureLogging(signalR.LogLevel.Error)
      .build();

    // New alert arrives → increment badge immediately
    this.alertConnection.on('NewAlert', () => {
      this.badge.increment();
    });

    // Alert dismissed → reload count from API to get accurate number
    this.alertConnection.on('AlertDismissed', () => {
      this.loadUnreadCount();
    });

    this.alertConnection.start().catch(() => {
      // Polling every 30s is the fallback if SignalR fails
    });
  }

  private async loadUnreadCount(): Promise<void> {
    try {
      const alerts = await firstValueFrom(this.api.getAlerts(undefined, false));
      this.badge.set(alerts.filter(a => !a.read).length);
    } catch { /* ignore */ }
  }
}
