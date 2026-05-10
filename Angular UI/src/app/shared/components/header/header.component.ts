import { Component, signal, OnInit, OnDestroy, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { ApiService } from '../../../services/api.service';
import { ThemeService } from '../../../services/theme.service';
import { CameraStatusService } from '../../../services/camera-status.service';
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

  unreadAlerts = signal(0);
  currentTime  = new Date();

  private clockInterval: ReturnType<typeof setInterval> | null = null;
  private pollInterval:  ReturnType<typeof setInterval> | null = null;
  private alertConnection: signalR.HubConnection | null = null;

  constructor(
    public auth: AuthService,
    public theme: ThemeService,
    private api: ApiService,
    private cameraStatus: CameraStatusService
  ) {}

  async ngOnInit(): Promise<void> {
    this.clockInterval = setInterval(() => this.currentTime = new Date(), 1000);

    // Initial load
    await this.loadUnreadCount();

    // Poll every 30s as fallback
    this.pollInterval = setInterval(() => this.loadUnreadCount(), 30000);

    // Real-time: listen for NewAlert on the alerts hub
    // Reuse the CameraStatusService connection (already connected to alerts hub)
    await this.cameraStatus.connect();
    this.subscribeToNewAlerts();
  }

  ngOnDestroy(): void {
    if (this.clockInterval) clearInterval(this.clockInterval);
    if (this.pollInterval)  clearInterval(this.pollInterval);
    if (this.alertConnection) {
      this.alertConnection.stop().catch(() => {});
      this.alertConnection = null;
    }
  }

  private subscribeToNewAlerts(): void {
    // Build a dedicated connection to the alerts hub just for the badge counter.
    // We can't reuse CameraStatusService's connection directly (no public .on() access),
    // so we open a lightweight second connection — it only listens for NewAlert events.
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

    // Every new alert → increment badge immediately (no API round-trip needed)
    this.alertConnection.on('NewAlert', () => {
      this.unreadAlerts.update(n => n + 1);
    });

    // When user navigates to alerts page and reads them, refresh the count
    this.alertConnection.on('AlertDismissed', () => {
      this.loadUnreadCount();
    });

    this.alertConnection.start().catch(() => {
      // If real-time fails, polling every 30s is the fallback
    });
  }

  private async loadUnreadCount(): Promise<void> {
    try {
      const alerts = await firstValueFrom(this.api.getAlerts(undefined, false));
      this.unreadAlerts.set(alerts.filter(a => !a.read).length);
    } catch { /* ignore */ }
  }
}
