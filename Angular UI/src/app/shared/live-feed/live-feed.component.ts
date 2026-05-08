import {
  Component, Input, OnInit, OnDestroy,
  ElementRef, ViewChild, signal, NgZone
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';
import { Camera } from '../../models';
import { environment } from '../../../environments/environment';
import * as signalR from '@microsoft/signalr';

@Component({
  selector: 'app-live-feed',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="live-feed-wrapper">

      <!-- Layer 1: video canvas — JPEG frames drawn here -->
      <canvas #videoCanvas class="video-canvas"></canvas>

      <!-- Layer 2: detection overlay — bounding boxes drawn here -->
      <canvas #overlayCanvas class="overlay-canvas"></canvas>

      <!-- State overlay — shown when not streaming -->
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

      <!-- LIVE badge -->
      @if (isStreaming()) {
        <div class="live-badge">
          <span class="live-dot"></span> LIVE
        </div>
      }

      <!-- FPS counter (dev aid) -->
      @if (isStreaming()) {
        <div class="fps-badge">{{ fps() }} fps</div>
      }
    </div>
  `,
  styles: [`
    .live-feed-wrapper {
      position: relative;
      width: 100%;
      height: 100%;
      background: #0a0a0f;
      border-radius: 6px;
      overflow: hidden;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .video-canvas {
      /* Let canvas render at its natural pixel size, wrapper clips it */
      max-width: 100%;
      max-height: 100%;
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
    }
    .overlay-canvas {
      position: absolute;
      top: 0; left: 0;
      max-width: 100%;
      max-height: 100%;
      width: 100%;
      height: 100%;
      pointer-events: none;
    }
    .feed-state {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 8px;
      color: #6b7280;
      font-size: 12px;
      background: #0a0a0f;
    }
    .spinner {
      width: 24px; height: 24px;
      border: 2px solid #374151;
      border-top-color: #3b82f6;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .retry-btn {
      margin-top: 4px;
      padding: 4px 12px;
      background: #1d4ed8;
      color: #fff;
      border: none;
      border-radius: 4px;
      font-size: 11px;
      cursor: pointer;
    }
    .retry-btn:hover { background: #2563eb; }
    .live-badge {
      position: absolute;
      top: 8px; right: 8px;
      display: flex;
      align-items: center;
      gap: 5px;
      background: rgba(0,0,0,0.6);
      color: #ef4444;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.05em;
      padding: 3px 7px;
      border-radius: 4px;
    }
    .live-dot {
      width: 6px; height: 6px;
      background: #ef4444;
      border-radius: 50%;
      animation: pulse 1.2s ease-in-out infinite;
    }
    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50%       { opacity: 0.3; }
    }
    .fps-badge {
      position: absolute;
      bottom: 8px; right: 8px;
      background: rgba(0,0,0,0.5);
      color: #9ca3af;
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 3px;
    }
  `]
})
export class LiveFeedComponent implements OnInit, OnDestroy {
  @Input({ required: true }) camera!: Camera;
  @Input() autoStart = true;

  @ViewChild('videoCanvas')  videoCanvasRef!:  ElementRef<HTMLCanvasElement>;
  @ViewChild('overlayCanvas') overlayCanvasRef!: ElementRef<HTMLCanvasElement>;

  isStreaming = signal(false);
  loading     = signal(false);
  error       = signal('');
  fps         = signal(0);

  private connection: signalR.HubConnection | null = null;
  private videoCtx:   CanvasRenderingContext2D | null = null;
  private overlayCtx: CanvasRenderingContext2D | null = null;

  // FPS tracking
  private frameCount  = 0;
  private fpsInterval: ReturnType<typeof setInterval> | null = null;

  // Bounding box auto-clear timer
  private clearOverlayTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private auth: AuthService, private zone: NgZone) {}

  ngOnInit(): void {
    if (this.autoStart && this.camera.rtspUrl) {
      // Delay to ensure ViewChild canvas refs are in the DOM
      setTimeout(() => this.connect(), 300);
    }
  }

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;

    this.loading.set(true);
    this.error.set('');

    const token = this.auth.getToken();

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.cameraStreamHubUrl, {
        accessTokenFactory: () => token ?? ''
      })
      .withAutomaticReconnect([0, 1000, 3000, 5000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // ── Receive video frame ──────────────────────────────────────────────────
    this.connection.on('ReceiveFrame', (data: {
      cameraId: string;
      frame: string;
      frameNum: number;
      timestamp: number;
    }) => {
      if (data.cameraId !== this.camera.id) return;
      if (data.frameNum === 1 || data.frameNum % 50 === 0)
        console.log(`[LiveFeed] frame #${data.frameNum}, base64 length: ${data.frame?.length}`);
      this.drawFrame(data.frame);
    });

    // ── Camera went offline ──────────────────────────────────────────────────
    this.connection.on('CameraOffline', (data: { cameraId: string }) => {
      if (data.cameraId !== this.camera.id) return;
      this.zone.run(() => {
        this.isStreaming.set(false);
        this.error.set('Camera disconnected');
      });
    });

    // ── Bounding boxes from AI ───────────────────────────────────────────────
    this.connection.on('BoundingBoxes', (data: {
      cameraId: string;
      detections: Array<{ label: string; confidence: number; x: number; y: number; w: number; h: number; severity: string }>;
    }) => {
      if (data.cameraId !== this.camera.id) return;
      this.drawBoundingBoxes(data.detections);
    });

    // ── Connection events ────────────────────────────────────────────────────
    this.connection.onreconnecting(() => {
      this.zone.run(() => this.isStreaming.set(false));
    });

    this.connection.onreconnected(async () => {
      await this.joinCamera();
    });

    try {
      await this.connection.start();
      await this.joinCamera();
      this.startFpsCounter();
      this.zone.run(() => this.loading.set(false));
    } catch (err: any) {
      this.zone.run(() => {
        this.loading.set(false);
        this.error.set('Could not connect to stream server');
      });
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.invoke('LeaveCamera', this.camera.id);
        await this.connection.stop();
      } catch { /* ignore */ }
      this.connection = null;
    }
    this.stopFpsCounter();
    this.isStreaming.set(false);
  }

  // ── Private helpers ───────────────────────────────────────────────────────

  private async joinCamera(): Promise<void> {
    try {
      await this.connection!.invoke('JoinCamera', this.camera.id);
    } catch (err) {
      this.zone.run(() => this.error.set('Failed to join camera stream'));
    }
  }

  private drawFrame(base64: string): void {
    const canvas = this.videoCanvasRef?.nativeElement;
    if (!canvas) return;

    if (!this.videoCtx) this.videoCtx = canvas.getContext('2d');
    const ctx = this.videoCtx;
    if (!ctx) return;

    const src = `data:image/jpeg;base64,${base64}`;

    // Try createImageBitmap first (GPU-accelerated, non-blocking)
    // Fall back to Image element if not supported
    if (typeof createImageBitmap !== 'undefined') {
      const raw   = atob(base64);
      const bytes = new Uint8Array(raw.length);
      for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);

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
          this.onFrameDrawn();
        })
        .catch(() => this.drawWithImage(src, canvas, ctx));
    } else {
      this.drawWithImage(src, canvas, ctx);
    }
  }

  private drawWithImage(
    src: string,
    canvas: HTMLCanvasElement,
    ctx: CanvasRenderingContext2D
  ): void {
    const img = new Image();
    img.onload = () => {
      if (canvas.width !== img.naturalWidth || canvas.height !== img.naturalHeight) {
        canvas.width  = img.naturalWidth  || canvas.width;
        canvas.height = img.naturalHeight || canvas.height;
        const ov = this.overlayCanvasRef?.nativeElement;
        if (ov) {
          ov.width  = canvas.width;
          ov.height = canvas.height;
          if (!this.overlayCtx) this.overlayCtx = ov.getContext('2d');
        }
      }
      ctx.drawImage(img, 0, 0);
      this.onFrameDrawn();
    };
    img.src = src;
  }

  private onFrameDrawn(): void {
    this.frameCount++;
    if (!this.isStreaming()) {
      this.zone.run(() => {
        this.isStreaming.set(true);
        this.loading.set(false);
        this.error.set('');
      });
    }
  }

  private drawBoundingBoxes(detections: Array<{
    label: string; confidence: number;
    x: number; y: number; w: number; h: number; severity: string;
  }>): void {
    const canvas = this.overlayCanvasRef?.nativeElement;
    const ctx    = this.overlayCtx;
    if (!canvas || !ctx) return;

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    const colorMap: Record<string, string> = {
      critical: '#ef4444',
      high:     '#f97316',
      medium:   '#eab308',
      low:      '#22c55e',
    };

    for (const det of detections) {
      const color = colorMap[det.severity] ?? '#3b82f6';

      // Bounding box
      ctx.strokeStyle = color;
      ctx.lineWidth   = 2;
      ctx.strokeRect(det.x, det.y, det.w, det.h);

      // Semi-transparent fill
      ctx.fillStyle = `${color}22`;
      ctx.fillRect(det.x, det.y, det.w, det.h);

      // Label background
      const label   = `${det.label} ${(det.confidence * 100).toFixed(0)}%`;
      ctx.font      = 'bold 12px sans-serif';
      const metrics = ctx.measureText(label);
      const lw      = metrics.width + 8;
      const lh      = 18;
      const lx      = det.x;
      const ly      = det.y > lh ? det.y - lh : det.y + det.h;

      ctx.fillStyle = color;
      ctx.fillRect(lx, ly, lw, lh);

      // Label text
      ctx.fillStyle = '#ffffff';
      ctx.fillText(label, lx + 4, ly + 13);
    }

    // Auto-clear boxes after 3 seconds if no new detections arrive
    if (this.clearOverlayTimer) clearTimeout(this.clearOverlayTimer);
    this.clearOverlayTimer = setTimeout(() => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
    }, 3000);
  }

  private startFpsCounter(): void {
    this.fpsInterval = setInterval(() => {
      this.zone.run(() => this.fps.set(this.frameCount));
      this.frameCount = 0;
    }, 1000);
  }

  private stopFpsCounter(): void {
    if (this.fpsInterval) {
      clearInterval(this.fpsInterval);
      this.fpsInterval = null;
    }
    this.fps.set(0);
  }

  ngOnDestroy(): void {
    this.disconnect();
    this.stopFpsCounter();
    if (this.clearOverlayTimer) clearTimeout(this.clearOverlayTimer);
  }
}
