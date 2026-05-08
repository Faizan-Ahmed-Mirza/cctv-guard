# AI-Powered CCTV Guard — Microservice Implementation Plan

## Architecture Summary

```
IP Camera (RTSP / DroidCam / mp4)
        ↓
[.NET Web API — Orchestrator]
  ├── LibVLCSharp/FFmpeg → RTSP stream proxy → Angular (HLS/WebRTC)
  ├── Frame extractor (1–4 FPS) → Python AI Microserver
  ├── Receives AI JSON results → creates Incident + Alert in SQL Server
  └── SignalR → broadcasts bounding boxes to Angular in real time
        ↓
[Python FastAPI — AI Worker]
  ├── YOLOv8  → detects fights, weapons, fire
  └── FaceNet → identity verification on detected persons
        ↓
[Angular Dashboard]
  ├── HTML5 video player (HLS stream)
  └── Canvas/SVG overlay → animated bounding boxes from SignalR
```

---

## Implementation Steps

---

### Step 1 — Python AI Microserver (FastAPI + YOLOv8 + FaceNet)

**Goal:** Standalone Python service that accepts a JPEG frame via HTTP POST and returns detection results as JSON.

**Tasks:**
- [ ] Create `ai-service/` Python project folder
- [ ] Set up `requirements.txt` (fastapi, uvicorn, ultralytics, deepface, opencv-python, pillow)
- [ ] Create `main.py` with FastAPI app
- [ ] `POST /detect` endpoint — accepts base64 or multipart JPEG frame, runs YOLOv8, returns bounding boxes + labels + confidence
- [ ] `POST /identify` endpoint — triggered when a "person" is detected, runs FaceNet identity check
- [ ] `GET /health` endpoint — returns service status and loaded model info
- [ ] Load YOLOv8n model on startup (auto-downloads weights)
- [ ] Return structured JSON: `{ detections: [{ label, confidence, x, y, w, h }] }`

**Output:** `http://localhost:8000` — Python AI service running locally

---

### Step 2 — .NET Frame Extractor & AI Bridge

**Goal:** .NET backend captures frames from RTSP/video source and forwards them to the Python AI service.

**Tasks:**
- [ ] Add `FFMpegCore` NuGet package to .NET project
- [ ] Create `FrameExtractorService` — connects to RTSP URL, extracts 1–4 FPS frames
- [ ] Create `AiProcessingService` — sends frames to `POST http://localhost:8000/detect`
- [ ] On detection result: create `Incident` + `Alert` records in SQL Server
- [ ] Trigger `HubNotificationService.SendNewAlertAsync()` to push via SignalR
- [ ] Create `POST /api/cameras/{id}/start-stream` and `stop-stream` endpoints
- [ ] Background service (`IHostedService`) that runs frame extraction per active camera

---

### Step 3 — .NET RTSP Stream Proxy (HLS/WebRTC for Angular)

**Goal:** .NET proxies the high-quality RTSP feed to Angular as HLS so the browser can play it natively.

**Tasks:**
- [ ] Add `FFMpegCore` to transcode RTSP → HLS segments (`.m3u8` + `.ts` files)
- [ ] Serve HLS segments via `GET /api/cameras/{id}/stream/index.m3u8`
- [ ] Store segments in temp folder, clean up old segments automatically
- [ ] Alternative: use WebRTC via `SIPSorcery` for ultra-low latency

---

### Step 4 — Angular Video Player + Bounding Box Overlay

**Goal:** Angular renders the live HLS stream and overlays AI bounding boxes from SignalR in real time.

**Tasks:**
- [ ] Install `hls.js` in Angular project (`npm install hls.js`)
- [ ] Create `LiveFeedComponent` with HTML5 `<video>` element + HLS.js integration
- [ ] Add transparent `<canvas>` overlay on top of the video element
- [ ] Connect to SignalR `AlertsHub` — subscribe to `NewAlert` and `BoundingBoxUpdate` events
- [ ] Draw bounding boxes on canvas using coordinates from SignalR payload
- [ ] CSS transitions for smooth box animation between frames
- [ ] Show label + confidence score above each bounding box
- [ ] Replace current static feed placeholder in `DashboardComponent` with `LiveFeedComponent`

---

### Step 5 — Camera Simulation (Development / Testing)

**Goal:** Simulate real IP cameras during development without physical hardware.

**Tasks:**
- [ ] Option A: Use **DroidCam** app on Android phone → provides RTSP URL on local network
- [ ] Option B: Use **IP Webcam** app → `rtsp://phone-ip:8080/h264_ulaw.sdp`
- [ ] Option C: Loop a local `.mp4` file as a fake RTSP stream using FFmpeg:
  ```bash
  ffmpeg -re -stream_loop -1 -i sample.mp4 -c copy -f rtsp rtsp://localhost:8554/cam-01
  ```
- [ ] Option D: Use **MediaMTX** (formerly rtsp-simple-server) as a local RTSP server
- [ ] Add camera RTSP URL field to the Configuration page camera form
- [ ] Update `Camera` entity to store `RtspUrl`

---

### Step 6 — Firebase Cloud Messaging (FCM) Push Notifications

**Goal:** Send mobile push notifications for high-priority detections (weapon, fight).

**Tasks:**
- [ ] Create Firebase project, download `google-services.json`
- [ ] Add `FirebaseAdmin` NuGet package to .NET project
- [ ] Create `FcmNotificationService` — sends push to registered device tokens
- [ ] `POST /api/notifications/register` — stores device FCM token per user
- [ ] Trigger FCM on `critical` or `high` severity incidents
- [ ] Add `DeviceTokens` table to SQL Server

---

### Step 7 — End-to-End Integration Testing

**Goal:** Verify the full pipeline works: camera → frame extraction → AI detection → incident logged → SignalR push → Angular overlay.

**Tasks:**
- [ ] Test with simulated camera (mp4 loop or DroidCam)
- [ ] Verify YOLOv8 detects objects and returns correct JSON
- [ ] Verify .NET creates Incident + Alert records in DB
- [ ] Verify SignalR pushes bounding box to Angular in < 2 seconds
- [ ] Verify bounding box renders correctly on canvas overlay
- [ ] Verify FCM notification arrives on mobile device
- [ ] Load test: 3+ simultaneous camera streams

---

## Current Status

| Step | Description | Status |
|------|-------------|--------|
| Step 1 | Python AI Microserver | ✅ Complete |
| Step 2 | .NET Frame Extractor & AI Bridge | ⏳ Pending |
| Step 3 | .NET RTSP Stream Proxy (HLS) | ⏳ Pending |
| Step 4 | Angular Video Player + Bounding Box Overlay | ⏳ Pending |
| Step 5 | Camera Simulation | ⏳ Pending |
| Step 6 | Firebase Push Notifications | ⏳ Pending |
| Step 7 | End-to-End Integration Testing | ⏳ Pending |

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| AI Inference | Python 3.11, FastAPI, Ultralytics YOLOv8, DeepFace/FaceNet |
| Frame Extraction | FFMpegCore (.NET), OpenCV (Python) |
| Stream Proxy | FFMpegCore → HLS, or SIPSorcery → WebRTC |
| Video Player | HTML5 `<video>` + HLS.js |
| Bounding Box Overlay | HTML5 Canvas API |
| Real-time Push | SignalR (bounding boxes), FCM (mobile alerts) |
| Camera Simulation | DroidCam, IP Webcam, FFmpeg RTSP loop, MediaMTX |
