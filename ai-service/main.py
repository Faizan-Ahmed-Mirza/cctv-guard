"""
CCTV Guard — AI Microserver
FastAPI service that accepts JPEG frames and returns YOLOv8 detections + FaceNet identity checks.

Endpoints:
  GET  /health          — service status and loaded model info
  POST /detect          — run YOLOv8 on a frame, returns bounding boxes
  POST /identify        — run FaceNet identity check on a cropped face region
  POST /register-face   — register a known person's face embedding
"""

import base64
import io
import logging
import os
import time
from contextlib import asynccontextmanager
from typing import Optional

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from PIL import Image
from pydantic import BaseModel
from ultralytics import YOLO

# ── Logging ────────────────────────────────────────────────────────────────────
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger(__name__)

# ── Model globals ──────────────────────────────────────────────────────────────
yolo_model: Optional[YOLO] = None
known_faces: dict[str, np.ndarray] = {}   # username → face embedding

# ── Label mapping: YOLO class → our incident type ─────────────────────────────
# YOLOv8n is trained on COCO — we map relevant classes.
# For production, swap with a custom-trained model for fight/weapon detection.
LABEL_MAP = {
    "person":     "person",
    "knife":      "weapon",
    "scissors":   "weapon",
    "gun":        "weapon",
    "pistol":     "weapon",
    "rifle":      "weapon",
    "fire":       "intrusion",
    "smoke":      "intrusion",
    "car":        "license_plate",
    "truck":      "license_plate",
    "motorcycle": "license_plate",
    "bus":        "license_plate",
}

# Severity mapping by incident type
SEVERITY_MAP = {
    "weapon":        "critical",
    "fight":         "critical",
    "unknown_face":  "high",
    "intrusion":     "medium",
    "license_plate": "low",
    "person":        "low",
}


# ── Startup / shutdown ─────────────────────────────────────────────────────────
@asynccontextmanager
async def lifespan(app: FastAPI):
    global yolo_model
    logger.info("Loading YOLOv8n model...")
    yolo_model = YOLO("yolov8n.pt")   # auto-downloads on first run
    logger.info("YOLOv8n model loaded successfully.")
    yield
    logger.info("AI Microserver shutting down.")


# ── FastAPI app ────────────────────────────────────────────────────────────────
app = FastAPI(
    title="CCTV Guard AI Microserver",
    description="YOLOv8 object detection + FaceNet identity verification",
    version="1.0.0",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


# ── Pydantic models ────────────────────────────────────────────────────────────
class BoundingBox(BaseModel):
    x: int
    y: int
    w: int
    h: int


class Detection(BaseModel):
    label: str          # e.g. "weapon", "person", "fight"
    yolo_class: str     # raw YOLO class name
    confidence: float   # 0.0 – 1.0
    severity: str       # critical | high | medium | low
    bounding_box: BoundingBox


class DetectResponse(BaseModel):
    camera_id: str
    timestamp: float
    detections: list[Detection]
    inference_ms: float


class IdentifyResponse(BaseModel):
    matched: bool
    username: Optional[str]
    confidence: float


class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    known_faces_count: int
    version: str


# ── Helpers ────────────────────────────────────────────────────────────────────
def decode_frame(data: str) -> np.ndarray:
    """Decode a base64-encoded JPEG string to an OpenCV BGR image."""
    img_bytes = base64.b64decode(data)
    img_array = np.frombuffer(img_bytes, dtype=np.uint8)
    img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("Failed to decode image from base64 data.")
    return img


def upload_to_cv2(file_bytes: bytes) -> np.ndarray:
    """Convert uploaded file bytes to an OpenCV BGR image."""
    img_array = np.frombuffer(file_bytes, dtype=np.uint8)
    img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("Failed to decode uploaded image.")
    return img


# ── Endpoints ──────────────────────────────────────────────────────────────────

@app.get("/health", response_model=HealthResponse, tags=["System"])
def health():
    """Returns service status and loaded model info."""
    return HealthResponse(
        status="ok",
        model_loaded=yolo_model is not None,
        known_faces_count=len(known_faces),
        version="1.0.0",
    )


@app.post("/detect", response_model=DetectResponse, tags=["Detection"])
async def detect(
    camera_id: str = Form(...),
    frame_base64: Optional[str] = Form(None),
    file: Optional[UploadFile] = File(None),
    confidence_threshold: float = Form(0.45),
):
    """
    Run YOLOv8 detection on a single frame.

    Accepts either:
    - `frame_base64`: base64-encoded JPEG string
    - `file`: multipart JPEG upload

    Returns bounding boxes, labels, confidence scores, and severity.
    """
    if yolo_model is None:
        raise HTTPException(status_code=503, detail="Model not loaded yet.")

    # Decode frame
    try:
        if frame_base64:
            img = decode_frame(frame_base64)
        elif file:
            img = upload_to_cv2(await file.read())
        else:
            raise HTTPException(status_code=400, detail="Provide frame_base64 or file.")
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    # Run YOLOv8 inference
    t0 = time.perf_counter()
    results = yolo_model(img, conf=confidence_threshold, verbose=False)
    inference_ms = (time.perf_counter() - t0) * 1000

    detections: list[Detection] = []
    for result in results:
        for box in result.boxes:
            cls_id    = int(box.cls[0])
            cls_name  = result.names[cls_id].lower()
            conf      = float(box.conf[0])
            x1, y1, x2, y2 = map(int, box.xyxy[0])

            # Map YOLO class to our incident label
            incident_label = LABEL_MAP.get(cls_name, cls_name)
            severity       = SEVERITY_MAP.get(incident_label, "low")

            detections.append(Detection(
                label=incident_label,
                yolo_class=cls_name,
                confidence=round(conf, 4),
                severity=severity,
                bounding_box=BoundingBox(
                    x=x1, y=y1,
                    w=x2 - x1,
                    h=y2 - y1,
                ),
            ))

    logger.info(
        "camera=%s detections=%d inference=%.1fms",
        camera_id, len(detections), inference_ms
    )

    return DetectResponse(
        camera_id=camera_id,
        timestamp=time.time(),
        detections=detections,
        inference_ms=round(inference_ms, 2),
    )


@app.post("/identify", response_model=IdentifyResponse, tags=["Face Recognition"])
async def identify(
    file: UploadFile = File(...),
    threshold: float = Form(0.6),
):
    """
    Run FaceNet identity check on a cropped face image.
    Compares against registered known faces.

    Returns matched username and confidence if a match is found.
    """
    if not known_faces:
        return IdentifyResponse(matched=False, username=None, confidence=0.0)

    try:
        from deepface import DeepFace

        img_bytes = await file.read()
        img_array = np.frombuffer(img_bytes, dtype=np.uint8)
        img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)

        # Get embedding for the input face
        embedding_result = DeepFace.represent(
            img_path=img,
            model_name="Facenet",
            enforce_detection=False,
        )
        if not embedding_result:
            return IdentifyResponse(matched=False, username=None, confidence=0.0)

        input_embedding = np.array(embedding_result[0]["embedding"])

        # Compare against known faces (cosine similarity)
        best_match: Optional[str] = None
        best_score = 0.0

        for username, known_embedding in known_faces.items():
            dot   = np.dot(input_embedding, known_embedding)
            norm  = np.linalg.norm(input_embedding) * np.linalg.norm(known_embedding)
            score = float(dot / norm) if norm > 0 else 0.0
            if score > best_score:
                best_score = score
                best_match = username

        matched = best_score >= threshold
        return IdentifyResponse(
            matched=matched,
            username=best_match if matched else None,
            confidence=round(best_score, 4),
        )

    except Exception as e:
        logger.error("Identity check failed: %s", e)
        return IdentifyResponse(matched=False, username=None, confidence=0.0)


@app.post("/register-face", tags=["Face Recognition"])
async def register_face(
    username: str = Form(...),
    file: UploadFile = File(...),
):
    """
    Register a known person's face embedding.
    Call this once per person to build the identity database.
    """
    try:
        from deepface import DeepFace

        img_bytes = await file.read()
        img_array = np.frombuffer(img_bytes, dtype=np.uint8)
        img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)

        result = DeepFace.represent(
            img_path=img,
            model_name="Facenet",
            enforce_detection=False,
        )
        if not result:
            raise HTTPException(status_code=400, detail="No face detected in the image.")

        known_faces[username] = np.array(result[0]["embedding"])
        logger.info("Registered face for user: %s", username)
        return {"message": f"Face registered for '{username}'.", "total_registered": len(known_faces)}

    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to register face: {e}")


# ── Entry point ────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
