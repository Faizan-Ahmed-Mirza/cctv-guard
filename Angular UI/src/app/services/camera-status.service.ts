import { Injectable, OnDestroy } from '@angular/core';
import { Subject } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

export interface CameraStatusEvent {
  id: string;
  status: 'online' | 'offline' | 'error';
}

/**
 * Singleton service that maintains a single SignalR connection to the alerts hub
 * and broadcasts CameraStatusChanged events to any subscriber.
 *
 * Connects automatically as soon as a JWT token is available.
 * Both DashboardComponent and ConfigurationComponent subscribe to statusChange$
 * so camera cards update in real-time without polling.
 */
@Injectable({ providedIn: 'root' })
export class CameraStatusService implements OnDestroy {
  private connection: signalR.HubConnection | null = null;
  private readonly _statusChange = new Subject<CameraStatusEvent>();
  private _connecting = false;

  // Cameras manually disabled by the user — health check won't override these
  private readonly _manuallyDisabled = new Set<string>();

  /** Subscribe to receive camera status updates pushed from the backend. */
  readonly statusChange$ = this._statusChange.asObservable();

  constructor(private auth: AuthService) {
    // Do NOT auto-connect here — the service is constructed before login.
    // connect() is called explicitly by components after login.
  }

  /** Mark a camera as manually disabled — health check won't flip it back to online. */
  setManuallyDisabled(cameraId: string, disabled: boolean): void {
    if (disabled) this._manuallyDisabled.add(cameraId);
    else          this._manuallyDisabled.delete(cameraId);
  }

  /** Connect to the alerts hub. Safe to call multiple times — idempotent. */
  async connect(): Promise<void> {
    if (this._connecting) return;
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;
    if (!this.auth.getAccessToken()) return;

    this._connecting = true;

    if (this.connection) {
      try { await this.connection.stop(); } catch { /* ignore */ }
      this.connection = null;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.getAccessToken() ?? '',
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      // Aggressive reconnect: 0ms, 1s, 2s, 5s, 10s — stays connected
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Error)
      .build();

    this.connection.on('CameraStatusChanged', (data: { id: string; status: string }) => {
      // If user manually disabled this camera, ignore health check 'online' events
      if (data.status === 'online' && this._manuallyDisabled.has(data.id)) return;

      this._statusChange.next({
        id:     data.id,
        status: data.status as CameraStatusEvent['status'],
      });
    });

    // On reconnect, re-subscribe (connection is already set up)
    this.connection.onreconnected(() => {
      console.debug('[CameraStatusService] Reconnected to alerts hub');
    });

    try {
      await this.connection.start();
    } catch (err) {
      console.warn('[CameraStatusService] SignalR connect failed:', err);
    } finally {
      this._connecting = false;
    }
  }

  /** Manually emit a status event — used when the UI triggers a stream start/stop directly. */
  emitStatus(id: string, status: CameraStatusEvent['status']): void {
    this._statusChange.next({ id, status });
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      try { await this.connection.stop(); } catch { /* ignore */ }
      this.connection = null;
    }
  }

  ngOnDestroy(): void {
    this.disconnect();
    this._statusChange.complete();
  }
}
