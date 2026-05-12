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

interface Detection {
  label: string;
  confidence: number;
  x: number; y: number;
  w: number; h: number;
  severity: string;
}

@Component({
  selector: 'app-live-feed',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="live-feed-wrapper" #wrapperDiv>
      <!-- Single canvas fills the wrapper exactly — no CSS scaling distortion -->
      <canvas #mainCanvas class="main-canvas"></canvas>

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
      background: #000; border-radius: 6px; overflow: hidden;
    }
    .main-canvas {
      /* Canvas fills the wrapper exactly — no object-fit, no distortion.
         Video is drawn scaled-to-fit inside the canvas via JS (letterbox math). */
      display: block; width: 100%; height: 100%;
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

  @ViewChild('mainCanvas')  mainCanvasRef!:  ElementRef<HTMLCanvasElement>;
  @ViewChild('wrapperDiv')  wrapperDivRef!:  ElementRef<HTMLDivElement>;

  isStreaming = signal(false);
  loading     = signal(false);
  error       = signal('');
  fps         = signal(0);

  private connection: signalR.HubConnection | null = null;
  private mainCtx:    CanvasRenderingContext2D | null = null;

  // Current detections — stored and redrawn on every frame
  private currentDetections: Detection[] = [];
  private boxExpiry = 0;

  // The rendered image rect inside the canvas (letterbox-aware)
  // Updated every time the canvas or bitmap size changes
  private renderRect = { x: 0, y: 0, w: 640, h: 480 };
  private bitmapW = 640;
  private bitmapH = 480;

  private frameCount   = 0;
  private fpsInterval: ReturnType<typeof setInterval> | null = null;
  private clearTimer:  ReturnType<typeof setTimeout> | null = null;
  private statusSub:   Subscription | null = null;
  private latestFrame: string | null = null;
  private renderScheduled = false;

  private cameraStatusService = inject(CameraStatusService);

  constructor(private auth: AuthService, private zone: NgZone) {}

  ngOnInit(): void {
    if (this.autoStart && this.camera.rtspUrl && this.camera.status === 'online') {
      this.loading.set(true);
      this.error.set('');
      this.connect();
    }

    this.statusSub = this.cameraStatusService.statusChange$.subscribe(event => {
      if (event.id !== this.camera.id) return;
      if (event.status === 'online' && this.autoStart && this.camera.rtspUrl) {
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
        this.clearBoxes();
      }
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['camera'] && !changes['camera'].firstChange) {
      const prev = changes['camera'].previousValue as Camera | undefined;
      const curr = changes['camera'].currentValue as Camera;
      if (prev?.status !== 'online' && curr?.status === 'online'
          && this.autoStart && curr.rtspUrl
          && this.connection?.state !== signalR.HubConnectionState.Connected) {
        this.loading.set(true);
        this.error.set('');
        this.connect();
      }
      if (prev?.status === 'online' && curr?.status !== 'online') {
        this.zone.run(() => {
          this.isStreaming.set(false);
          this.loading.set(false);
          this.error.set('');
          this.stopFpsCounter();
        });
        this.clearBoxes();
      }
    }
  }

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;
    if (this.connection) {
      try { await this.connection.stop(); } catch { }
      this.connection = null;
    }

    this.loading.set(true);
    this.error.set('');
    this.isStreaming.set(false);

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.cameraStreamHubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? '',
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Error)
      .build();

    this.connection.on('ReceiveFrame', (data: {
      cameraId: string; frame: string; frameNum: number; timestamp: number;
    }) => {
      if (data.cameraId !== this.camera.id) return;
      // Drop frames older than 1.5s (buffered/stale)
      if (Date.now() - data.timestamp > 1500) return;
      this.latestFrame = data.frame;
      if (!this.renderScheduled) {
        this.renderScheduled = true;
        requestAnimationFrame(() => this.renderLatestFrame());
      }
    });

    this.connection.on('BoundingBoxes', (data: {
      cameraId: string; detections: Detection[];
    }) => {
      if (data.cameraId !== this.camera.id) return;
      if (!data.detections || data.detections.length === 0) return;
      this.currentDetections = data.detections;
      this.boxExpiry = Date.now() + 4000;
      if (this.clearTimer) clearTimeout(this.clearTimer);
      this.clearTimer = setTimeout(() => {
        this.currentDetections = [];
        this.boxExpiry = 0;
      }, 4000);
    });

    this.connection.on('CameraOffline', (data: { cameraId: string }) => {
      if (data.cameraId !== this.camera.id) return;
      this.zone.run(() => {
        this.isStreaming.set(false);
        this.error.set('Camera disconnected');
        this.stopFpsCounter();
      });
      this.clearBoxes();
    });

    this.connection.onreconnecting(() => this.zone.run(() => {
      this.isStreaming.set(false);
      this.loading.set(true);
      this.error.set('');
      this.stopFpsCounter();
    }));

    this.connection.onreconnected(async () => {
      try {
        await this.connection!.invoke('JoinCamera', this.camera.id);
        this.zone.run(() => this.loading.set(false));
      } catch {
        this.zone.run(() => { this.loading.set(false); this.error.set('Could not rejoin camera stream'); });
      }
    });

    this.connection.onclose(() => this.zone.run(() => {
      this.isStreaming.set(false);
      this.loading.set(false);
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
      try { await conn.invoke('LeaveCamera', this.camera.id); await conn.stop(); } catch { }
    }
    this.stopFpsCounter();
    this.isStreaming.set(false);
  }

  // ── Rendering ─────────────────────────────────────────────────────────────

  private renderLatestFrame(): void {
    this.renderScheduled = false;
    const frame = this.latestFrame;
    this.latestFrame = null;
    if (!frame) return;

    const canvas = this.mainCanvasRef?.nativeElement;
    if (!canvas) return;
    if (!this.mainCtx) this.mainCtx = canvas.getContext('2d');
    const ctx = this.mainCtx;
    if (!ctx) return;

    const raw   = atob(frame);
    const bytes = Uint8Array.from(raw, c => c.charCodeAt(0));

    createImageBitmap(new Blob([bytes], { type: 'image/jpeg' }))
      .then(bitmap => {
        // Sync canvas internal size to its CSS display size (wrapper size)
        // This ensures 1 canvas pixel = 1 screen pixel — no distortion
        const displayW = canvas.clientWidth  || canvas.offsetWidth  || 640;
        const displayH = canvas.clientHeight || canvas.offsetHeight || 480;

        if (canvas.width !== displayW || canvas.height !== displayH) {
          canvas.width  = displayW;
          canvas.height = displayH;
        }

        // Calculate letterbox rect — same math as incident thumbnail
        // This is where the video image actually appears inside the canvas
        if (this.bitmapW !== bitmap.width || this.bitmapH !== bitmap.height) {
          this.bitmapW = bitmap.width;
          this.bitmapH = bitmap.height;
        }
        this.renderRect = this.calcRenderRect(displayW, displayH, bitmap.width, bitmap.height);

        // Fill background black (letterbox bars)
        ctx.fillStyle = '#000';
        ctx.fillRect(0, 0, displayW, displayH);

        // Draw video frame scaled to fit
        ctx.drawImage(bitmap, this.renderRect.x, this.renderRect.y,
                              this.renderRect.w, this.renderRect.h);
        bitmap.close();

        // Draw bounding boxes on top — using the same renderRect for coordinate mapping
        if (this.currentDetections.length > 0 && Date.now() < this.boxExpiry) {
          this.drawBoxes(ctx, this.renderRect, bitmap.width || this.bitmapW, bitmap.height || this.bitmapH);
        }

        this.frameCount++;

        if (!this.isStreaming()) {
          this.zone.run(() => {
            this.isStreaming.set(true);
            this.loading.set(false);
            this.error.set('');
            this.startFpsCounter();
          });
        }

        if (this.latestFrame) {
          this.renderScheduled = true;
          requestAnimationFrame(() => this.renderLatestFrame());
        }
      })
      .catch(() => {
        const img = new Image();
        img.onload = () => {
          const displayW = canvas.clientWidth  || 640;
          const displayH = canvas.clientHeight || 480;
          if (canvas.width !== displayW || canvas.height !== displayH) {
            canvas.width = displayW; canvas.height = displayH;
          }
          this.bitmapW = img.naturalWidth;
          this.bitmapH = img.naturalHeight;
          this.renderRect = this.calcRenderRect(displayW, displayH, img.naturalWidth, img.naturalHeight);
          ctx!.fillStyle = '#000';
          ctx!.fillRect(0, 0, displayW, displayH);
          ctx!.drawImage(img, this.renderRect.x, this.renderRect.y, this.renderRect.w, this.renderRect.h);
          if (this.currentDetections.length > 0 && Date.now() < this.boxExpiry) {
            this.drawBoxes(ctx!, this.renderRect, img.naturalWidth, img.naturalHeight);
          }
          this.frameCount++;
          if (!this.isStreaming())
            this.zone.run(() => { this.isStreaming.set(true); this.loading.set(false); this.startFpsCounter(); });
        };
        img.src = `data:image/jpeg;base64,${frame}`;
      });
  }

  /**
   * Calculate the rendered image rect inside the canvas using object-fit:contain logic.
   * Returns { x, y, w, h } — the pixel rect where the video image is drawn.
   */
  private calcRenderRect(canvasW: number, canvasH: number, imgW: number, imgH: number)
      : { x: number; y: number; w: number; h: number } {
    const imgAspect    = imgW / imgH;
    const canvasAspect = canvasW / canvasH;
    let rw: number, rh: number, rx: number, ry: number;
    if (imgAspect > canvasAspect) {
      // Image wider — fit to width, letterbox top/bottom
      rw = canvasW;
      rh = canvasW / imgAspect;
      rx = 0;
      ry = (canvasH - rh) / 2;
    } else {
      // Image taller — fit to height, pillarbox left/right
      rh = canvasH;
      rw = canvasH * imgAspect;
      rx = (canvasW - rw) / 2;
      ry = 0;
    }
    return { x: rx, y: ry, w: rw, h: rh };
  }

  /**
   * Draw bounding boxes directly onto the main canvas context.
   * AI coordinates are in bitmap space (e.g. 640×480).
   * We scale them to the renderRect (where the video is actually drawn).
   */
  private drawBoxes(
    ctx: CanvasRenderingContext2D,
    rect: { x: number; y: number; w: number; h: number },
    bitmapW: number,
    bitmapH: number
  ): void {
    const scaleX = rect.w / bitmapW;
    const scaleY = rect.h / bitmapH;

    const colors: Record<string, string> = {
      critical: '#ef4444',
      high:     '#f97316',
      medium:   '#eab308',
      low:      '#22c55e',
    };

    for (const d of this.currentDetections) {
      const c = colors[d.severity] ?? '#3b82f6';

      // Map AI bitmap coords → canvas screen coords
      const sx = rect.x + d.x * scaleX;
      const sy = rect.y + d.y * scaleY;
      const sw = d.w * scaleX;
      const sh = d.h * scaleY;

      if (sw <= 0 || sh <= 0) continue;

      // Box
      ctx.strokeStyle = c;
      ctx.lineWidth   = 2.5;
      ctx.strokeRect(sx, sy, sw, sh);
      ctx.fillStyle = `${c}30`;
      ctx.fillRect(sx, sy, sw, sh);

      // Label badge
      const label = `${d.label.replace(/_/g, ' ')} ${(d.confidence * 100).toFixed(0)}%`;
      ctx.font = 'bold 13px sans-serif';
      const tw = ctx.measureText(label).width + 10;
      const th = 20;
      const ly = sy > th ? sy - th : sy + sh;
      ctx.fillStyle = c;
      ctx.fillRect(sx, ly, tw, th);
      ctx.fillStyle = '#fff';
      ctx.fillText(label, sx + 5, ly + 14);
    }
  }

  private clearBoxes(): void {
    this.currentDetections = [];
    this.boxExpiry = 0;
    if (this.clearTimer) { clearTimeout(this.clearTimer); this.clearTimer = null; }
    const canvas = this.mainCanvasRef?.nativeElement;
    if (canvas && this.mainCtx) {
      this.mainCtx.fillStyle = '#000';
      this.mainCtx.fillRect(0, 0, canvas.width, canvas.height);
    }
    this.latestFrame = null;
    this.renderScheduled = false;
  }

  private startFpsCounter(): void {
    if (this.fpsInterval) return;
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
    if (this.connection) {
      const conn = this.connection;
      this.connection = null;
      conn.stop().catch(() => { });
    }
    this.statusSub?.unsubscribe();
    this.stopFpsCounter();
    if (this.clearTimer) clearTimeout(this.clearTimer);
    this.latestFrame = null;
    this.renderScheduled = false;
    this.currentDetections = [];
  }
}
