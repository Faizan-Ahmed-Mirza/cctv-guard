# CCTV Guard — AI Microserver

Python FastAPI service for YOLOv8 object detection and FaceNet identity verification.

## Setup

### 1. Create virtual environment
```bash
cd ai-service
python -m venv venv

# Windows
venv\Scripts\activate

# Linux/Mac
source venv/bin/activate
```

### 2. Install dependencies
```bash
pip install -r requirements.txt
```
> YOLOv8 weights (`yolov8n.pt`) download automatically on first run (~6 MB).

### 3. Run the server
```bash
python main.py
```
Server starts at `http://localhost:8000`
Swagger UI at `http://localhost:8000/docs`

---

## Endpoints

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/health` | Service status + model info | None |
| POST | `/detect` | Run YOLOv8 on a frame | None |
| POST | `/identify` | FaceNet identity check | None |
| POST | `/register-face` | Register a known face | None |

---

## POST /detect

Accepts a JPEG frame (base64 or multipart upload) and returns detections.

**Form fields:**
- `camera_id` (string, required) — camera identifier
- `frame_base64` (string, optional) — base64-encoded JPEG
- `file` (file, optional) — multipart JPEG upload
- `confidence_threshold` (float, default 0.45) — minimum confidence

**Response:**
```json
{
  "camera_id": "cam-01",
  "timestamp": 1700000000.0,
  "inference_ms": 12.4,
  "detections": [
    {
      "label": "weapon",
      "yolo_class": "knife",
      "confidence": 0.91,
      "severity": "critical",
      "bounding_box": { "x": 120, "y": 80, "w": 60, "h": 90 }
    }
  ]
}
```

**Label → Severity mapping:**

| Label | Severity |
|-------|----------|
| weapon | critical |
| fight | critical |
| unknown_face | high |
| intrusion | medium |
| license_plate | low |

---

## POST /register-face

Register a known person's face for identity verification.

**Form fields:**
- `username` (string) — person's username
- `file` (file) — JPEG photo of the person's face

---

## POST /identify

Check if a detected face matches a registered person.

**Form fields:**
- `file` (file) — cropped face JPEG
- `threshold` (float, default 0.6) — cosine similarity threshold

---

## Notes

- YOLOv8n is the nano model (fastest, ~6 MB). For better accuracy use `yolov8s.pt` or `yolov8m.pt`.
- The current model is trained on COCO classes. For fight detection, a custom-trained model is needed.
- FaceNet embeddings are stored in memory — they reset on restart. For production, store in the SQL Server `FacialEmbeddings` table.
- GPU acceleration is automatic if CUDA is available (`torch` detects it).
