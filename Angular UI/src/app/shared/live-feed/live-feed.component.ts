import {
  Component, Input, OnInit, OnDestroy, OnChanges, SimpleChanges,
  ElementRef, ViewChild, signal, NgZone, ChangeDetectionStrategy, inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';
import { Camera } from '../../models';
import { environment } from '../../../environments/environment';
import { CameraStatusService } from '../../services/camera-status.service';
import { Subscription } from 'rxjs';
import * as signalR from '@microsoft/signalr';

@Component({
  selector: 'app-live-feed',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush, // only re-render on signal changes
  template: `
    <div class="live-feed-wrapper">
      <canvas #videoCanvas class="video-canvas"></canvas>
      <canvas #overlayCanvas class="overlay-canvas"></canvas>

      @if (!isStreaming()) {
        <div class="feed-state">
          @if (loading()) {
            <div class="spinner"></div>
            <span>Connecting...</span>
          } @else if (error()) {
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <line x1="1" y1="1" x2="23" y2="23"/>
              <path d="M21 21H3a2 2 0 01-2-2V8a2 2 0 012-2h3m3-3h6l2 3h4a2 2 0 012 2v9.34"/>
            </svg>
            <span>{{ error() }}</span>
            <button class="retry-btn" (click)="connect()">Retry</button>
          } @else {
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <line x1="1" y1="1" x2="23" y2="23"/>
              <path d="M21 21H3a2 2 0 01-2-2V8a2 2 0 012-2h3m3-3h6l2 3h4a2 2 0 012 2v9.34"/>
            </svg>
            <span>{{ camera.rtspUrl ? 'Camera Offline' : 'No Stream URL' }}</span>
          }
        </div>
      }

      @if (isStreaming()) {
        <div class="live-badge"><span class="live-dot"></span> LIVE</div>
        <div class="fps-badge">{{ fps() }} fps</div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; height: 100%; }
    .live-feed-wrapper {
      position: relative; width: 100%; height: 100%;
      background: #0a0a0f; border-radius: 6px; overflow: hidden;
    }
    .video-canvas {
      width: 100%; height: 100%; display: block; object-fit: cover;
    }
    .overlay-canvas {
      position: absolute; top: 0; left: 0;
      width: 100%; height: 100%; pointer-events: none;
    }
    .feed-state {
      position: absolute; inset: 0; display: flex; flex-direction: column;
      align-items: center; justify-content: center; gap: 8px;
      color: #6b7280; font-size: 12px; background: #0a0a0f;
    }
    .spinner {
      width: 24px; height: 24px; border: 2px solid #374151;
      border-top-color: #3b82f6; border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .retry-btn {
      margin-top: 4px; padding: 4px 12px; background: #1d4ed8;
      color: #fff; border: none; border-radius: 4px; font-size: 11px; cursor: pointer;
    }
    .live-badge {
      position: absolute; top: 8px; right: 8px; display: flex; align-items: center;
      gap: 5px; background: rgba(0,0,0,0.6); color: #ef4444;
      font-size: 10px; font-weight: 700; padding: 3px 7px; border-radius: 4px;
    }
    .live-dot {
      width: 6px; height: 6px; background: #ef4444; border-radius: 50%;
      animation: pulse 1.2s ease-in-out infinite;
    }
    @keyframes pulse { 0%,100% { opacity:1; } 50% { opacity:0.3; } }
    .fps-badge {
      position: absolute; bottom: 8px; right: 8px; background: rgba(0,0,0,0.5);
      color: #9ca3af; font-size: 10px; padding: 2px 6px; border-radius: 3px;
    }
  `]
})
export class LiveFeedComponent implements OnInit, OnChanges, OnDestroy {
  @Input({ required: true }) camera!: Camera;
  @Input() autoStart = true;

  @ViewChild('videoCanvas')   videoCanvasRef!:   ElementRef<HTMLCanvasElement>;
  @ViewChild('overlayCanvas') overlayCanvasRef!: ElementRef<HTMLCanvasElement>;

  isStreaming = signal(false);
  loading     = signal(false);
  error       = signal('');
  fps         = signal(0);

  private connection:  signalR.HubConnection | null = null;
  private videoCtx:    CanvasRenderingContext2D | null = null;
  private overlayCtx:  CanvasRenderingContext2D | null = null;
  private frameCount   = 0;
  private fpsInterval: ReturnType<typeof setInterval> | null = null;
  private clearTimer:  ReturnType<typeof setTimeout> | null = null;
  private statusSub:   Subscription | null = null;

  // Frame queue — only keep the latest frame, drop stale ones
  private latestFrame: string | null = null;
  private renderScheduled = false;

  private cameraStatusService = inject(CameraStatusService);

  constructor(private auth: AuthService, private zone: NgZone) {}

  ngOnInit(): void {
    // Auto-start if camera is online — covers both first load and navigation back
    if (this.autoStart && this.camera.rtspUrl && this.camera.status === 'online') {
      this.loading.set(true);
      this.error.set('');
      this.connect();
    }

    // Subscribe to real-time status changes from SignalR
    this.statusSub = this.cameraStatusService.statusChange$.subscribe(event => {
      if (event.id !== this.camera.id) return;

      if (event.status === 'online' && this.autoStart && this.camera.rtspUrl) {
        // Only connect if not already streaming
        if (this.connection?.state !== signalR.HubConnectionState.Connected) {
          this.loading.set(true);
          this.error.set('');
          this.connect();
        }
      } else if (event.status === 'offline' || event.status === 'error') {
        this.zone.run(() => {
          this.isStreaming.set(false);
          this.loading.set(false);
          this.error.set('');
          this.stopFpsCounter();
        });
        this.clearCanvas();
      }
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    // React when parent updates the camera object (e.g. after toggleStream)
    if (changes['camera'] && !changes['camera'].firstChange) {
      const prev = changes['camera'].previousValue as Camera | undefined;
      const curr = changes['camera'].currentValue as Camera;

      // Camera just became online from parent update — start stream
      if (prev?.status !== 'online' && curr?.status === 'online'
          && this.autoStart && curr.rtspUrl
          && this.connection?.state !== signalR.HubConnectionState.Connected) {
        this.loading.set(true);
        this.error.set('');
        this.connect();
      }

      // Camera went offline from parent update — clear streaming state and canvas
      if (prev?.status === 'online' && curr?.status !== 'online') {
        this.zone.run(() => {
          this.isStreaming.set(false);
          this.loading.set(false);
          this.error.set('');
          this.stopFpsCounter();
        });
        this.clearCanvas();
      }
    }
  }

  async connect(): Promise<void> {
    // If already connected, skip
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;

    // Tear down any existing (failed/disconnected) connection before retrying
    if (this.connection) {
      try { await this.connection.stop(); } catch { /* ignore */ }
      this.connection = null;
    }

    this.loading.set(true);
    this.error.set('');
    this.isStreaming.set(false);

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.cameraStreamHubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? '',
        // Use WebSockets only — skip long-polling fallback for lower latency
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Error) // only log errors
      .build();

    // ── Receive video frame — runs OUTSIDE Angular zone for max performance ──
    this.connection.on('ReceiveFrame', (data: {
      cameraId: string; frame: string; frameNum: number; timestamp: number;
    }) => {
      if (data.cameraId !== this.camera.id) return;

      // Drop frames older than 500ms — keeps the feed truly live.
      // If the server is sending faster than we can render, always show the newest.
      const age = Date.now() - data.timestamp;
      if (age > 500) return; // stale frame — skip it

      // Store latest frame — if render is already scheduled, it will pick this up
      this.latestFrame = data.frame;
      if (!this.renderScheduled) {
        this.renderScheduled = true;
        requestAnimationFrame(() => this.renderLatestFrame());
      }
    });

    this.connection.on('CameraOffline', (data: { cameraId: string }) => {
      if (data.cameraId !== this.camera.id) return;
      this.zone.run(() => {
        this.isStreaming.set(false);
        this.error.set('Camera disconnected');
        this.stopFpsCounter();
      });
      this.clearCanvas(); // immediately black out the video — no frozen last frame
    });

    this.connection.on('BoundingBoxes', (data: {
      cameraId: string;
      detections: Array<{ label: string; confidence: number; x: number; y: number; w: number; h: number; severity: string }>;
    }) => {
      if (data.cameraId !== this.camera.id) return;
      this.drawBoundingBoxes(data.detections);
    });

    this.connection.onreconnecting(() =>
      this.zone.run(() => {
        this.isStreaming.set(false);
        this.loading.set(true);
        this.error.set('');
        this.stopFpsCounter(); // stop counter — will restart when frames arrive again
      }));

    this.connection.onreconnected(async () => {
      try {
        await this.connection!.invoke('JoinCamera', this.camera.id);
        this.zone.run(() => this.loading.set(false));
      } catch {
        this.zone.run(() => { this.loading.set(false); this.error.set('Could not rejoin camera stream'); });
      }
    });

    this.connection.onclose(() =>
      this.zone.run(() => {
        this.isStreaming.set(false);
        this.loading.set(false);
        // Don't show error on close — just show offline state silently
        // so when user navigates back it auto-reconnects cleanly
        this.error.set('');
      }));

    try {
      await this.connection.start();
      await this.connection.invoke('JoinCamera', this.camera.id);
      this.zone.run(() => this.loading.set(false));
    } catch (err) {
      console.error(`[LiveFeed] ${this.camera.id}: connection failed`, err);
      this.zone.run(() => {
        this.loading.set(false);
        this.error.set('Could not connect to stream server');
      });
    }
  }

  async disconnect(): Promise<void> {
    const conn = this.connection;
    this.connection = null;
    if (conn) {
      try {
        await conn.invoke('LeaveCamera', this.camera.id);
        await conn.stop();
      } catch { /* ignore */ }
    }
    this.stopFpsCounter();
    this.isStreaming.set(false);
  }

  // ── Rendering — runs outside Angular zone, synced to browser paint cycle ──

  private renderLatestFrame(): void {
    this.renderScheduled = false;
    const frame = this.latestFrame;
    this.latestFrame = null;
    if (!frame) return;

    const canvas = this.videoCanvasRef?.nativeElement;
    if (!canvas) return;
    if (!this.videoCtx) this.videoCtx = canvas.getContext('2d');
    const ctx = this.videoCtx;
    if (!ctx) return;

    // createImageBitmap decodes JPEG off the main thread (GPU-accelerated)
    const raw   = atob(frame);
    const bytes = Uint8Array.from(raw, c => c.charCodeAt(0));

    createImageBitmap(new Blob([bytes], { type: 'image/jpeg' }))
      .then(bitmap => {
        if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
          canvas.width  = bitmap.width;
          canvas.height = bitmap.height;
          const ov = this.overlayCanvasRef?.nativeElement;
          if (ov) {
            ov.width  = bitmap.width;
            ov.height = bitmap.height;
            if (!this.overlayCtx) this.overlayCtx = ov.getContext('2d');
          }
        }
        ctx.drawImage(bitmap, 0, 0);
        bitmap.close();
        this.frameCount++;

        if (!this.isStreaming()) {
          this.zone.run(() => {
            this.isStreaming.set(true);
            this.loading.set(false);
            this.error.set('');
            this.startFpsCounter(); // start counting only once real frames arrive
          });
        }

        // If another frame arrived while we were decoding, render it now
        if (this.latestFrame) {
          this.renderScheduled = true;
          requestAnimationFrame(() => this.renderLatestFrame());
        }
      })
      .catch(() => {
        // Fallback to Image element
        const img = new Image();
        img.onload = () => {
          if (canvas.width !== img.naturalWidth) {
            canvas.width  = img.naturalWidth;
            canvas.height = img.naturalHeight;
          }
          ctx.drawImage(img, 0, 0);
          this.frameCount++;
          if (!this.isStreaming())
            this.zone.run(() => {
              this.isStreaming.set(true);
              this.loading.set(false);
              this.startFpsCounter();
            });
        };
        img.src = `data:image/jpeg;base64,${frame}`;
      });
  }

  private drawBoundingBoxes(detections: Array<{
    label: string; confidence: number; x: number; y: number; w: number; h: number; severity: string;
  }>): void {
    const canvas = this.overlayCanvasRef?.nativeElement;
    if (!canvas) return;
    if (!this.overlayCtx) this.overlayCtx = canvas.getContext('2d');
    const ctx = this.overlayCtx;
    if (!ctx) return;

    ctx.clearRect(0, 0, canvas.width, canvas.height);
    const colors: Record<string, string> = {
      critical: '#ef4444', high: '#f97316', medium: '#eab308', low: '#22c55e'
    };

    for (const d of detections) {
      const c = colors[d.severity] ?? '#3b82f6';
      ctx.strokeStyle = c; ctx.lineWidth = 2;
      ctx.strokeRect(d.x, d.y, d.w, d.h);
      ctx.fillStyle = `${c}22`;
      ctx.fillRect(d.x, d.y, d.w, d.h);

      const label = `${d.label} ${(d.confidence * 100).toFixed(0)}%`;
      ctx.font = 'bold 12px sans-serif';
      const lw = ctx.measureText(label).width + 8;
      const ly = d.y > 18 ? d.y - 18 : d.y + d.h;
      ctx.fillStyle = c;
      ctx.fillRect(d.x, ly, lw, 18);
      ctx.fillStyle = '#fff';
      ctx.fillText(label, d.x + 4, ly + 13);
    }

    if (this.clearTimer) clearTimeout(this.clearTimer);
    this.clearTimer = setTimeout(() => ctx.clearRect(0, 0, canvas.width, canvas.height), 3000);
  }

  // ── Canvas helpers ────────────────────────────────────────────────────────

  /** Immediately black out both canvases — called when camera goes offline. */
  private clearCanvas(): void {
    const video = this.videoCanvasRef?.nativeElement;
    if (video && this.videoCtx) {
      this.videoCtx.fillStyle = '#000';
      this.videoCtx.fillRect(0, 0, video.width, video.height);
    }
    const overlay = this.overlayCanvasRef?.nativeElement;
    if (overlay && this.overlayCtx) {
      this.overlayCtx.clearRect(0, 0, overlay.width, overlay.height);
    }
    // Drop any pending frame so it doesn't render after the clear
    this.latestFrame = null;
    this.renderScheduled = false;
  }

  private startFpsCounter(): void {
    if (this.fpsInterval) return; // already running — don't double-start
    this.fpsInterval = setInterval(() => {
      this.zone.run(() => this.fps.set(this.frameCount));
      this.frameCount = 0;
    }, 1000);
  }

  private stopFpsCounter(): void {
    if (this.fpsInterval) { clearInterval(this.fpsInterval); this.fpsInterval = null; }
    this.fps.set(0);
  }

  ngOnDestroy(): void {
    // Disconnect SignalR but do NOT call LeaveCamera — this keeps FFmpeg running
    // on the server so the stream stays alive when the user navigates back.
    // The CameraStreamHub will clean up via OnDisconnectedAsync automatically.
    if (this.connection) {
      const conn = this.connection;
      this.connection = null;
      conn.stop().catch(() => { /* ignore */ });
    }
    this.statusSub?.unsubscribe();
    this.stopFpsCounter();
    if (this.clearTimer) clearTimeout(this.clearTimer);
    this.latestFrame = null;
    this.renderScheduled = false;
  }
}
