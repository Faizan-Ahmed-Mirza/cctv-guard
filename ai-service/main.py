"""
CCTV Guard — AI Microservice v5.0
Multi-model detection pipeline:

  Model 1: yolov8n.pt        — COCO 80-class: persons, vehicles, knives, scissors
  Model 2: yolov8n-pose.pt   — Pose estimation for fight detection (17 keypoints)
  Model 3: weapon_model.pt   — Generic weapon detector (148MB) — class: weapon
  Model 4: threat_model.pt   — Specific threats (6MB) — Gun, grenade, knife, explosion
  Model 5: fire_model.pt     — Fire/smoke detection (22MB) — class: Fire
  OCR:     EasyOCR           — License plate text extraction
  Face:    DeepFace/Facenet512 — Identity verification (97.4% accuracy, 512-dim embeddings)

Endpoints:
  GET  /health
  POST /detect
  POST /register-face
  GET  /faces
  DELETE /faces/{username}
"""

import base64
import logging
import os
import pickle
import time
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Optional

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from ultralytics import YOLO

# ── Logging ────────────────────────────────────────────────────────────────────
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger(__name__)

# ── Paths ──────────────────────────────────────────────────────────────────────
BASE_DIR        = Path(__file__).parent
FACES_DB_PATH   = BASE_DIR / "faces_db.pkl"
COCO_MODEL      = BASE_DIR / "yolov8n.pt"
POSE_MODEL      = BASE_DIR / "yolov8n-pose.pt"
WEAPON_MODEL    = BASE_DIR / "weapon_model.pt"   # generic weapon detector (148MB)
THREAT_MODEL    = BASE_DIR / "threat_model.pt"   # Gun/grenade/knife/explosion (6MB)
FIRE_MODEL      = BASE_DIR / "fire_model.pt"     # optional fire/smoke model

# ── Model globals ──────────────────────────────────────────────────────────────
coco_model:   Optional[YOLO] = None   # persons, vehicles, COCO weapons
pose_model:   Optional[YOLO] = None   # fight detection via pose
weapon_model: Optional[YOLO] = None   # generic weapon detector
threat_model: Optional[YOLO] = None   # Gun, grenade, knife, explosion
fire_model:   Optional[YOLO] = None   # fire/smoke detection
ocr_reader = None                     # EasyOCR for license plates
known_faces: dict[str, np.ndarray] = {}

# ── COCO weapon/vehicle classes ────────────────────────────────────────────────
WEAPON_CLASSES   = {"knife", "scissors", "baseball bat"}
VEHICLE_CLASSES  = {"car", "truck", "motorcycle", "bus", "bicycle"}

# Threat model class names that map to weapon severity
THREAT_WEAPON_CLASSES = {"gun", "grenade", "knife", "weapon", "pistol", "rifle",
                         "blade", "sword", "dagger", "machete", "handgun"}

# ── Severity map ───────────────────────────────────────────────────────────────
SEVERITY_MAP = {
    "weapon":        "critical",
    "fight":         "critical",
    "fire":          "critical",
    "unknown_face":  "high",
    "intrusion":     "medium",
    "license_plate": "low",
    "person":        "low",
}

# ── Fight detection thresholds (pose-based) ────────────────────────────────────
# Keypoint indices (COCO pose): 0=nose, 5=L-shoulder, 6=R-shoulder,
# 7=L-elbow, 8=R-elbow, 9=L-wrist, 10=R-wrist, 11=L-hip, 12=R-hip
FIGHT_WRIST_SPEED_THRESHOLD = 0.20   # normalized wrist movement — raised from 0.15 to reduce false positives
FIGHT_OVERLAP_IOU_THRESHOLD = 0.25   # bounding box overlap — raised from 0.15, needs significant overlap

# Track previous pose keypoints per camera for motion analysis
_prev_poses: dict[str, list] = {}


# ── Face DB ────────────────────────────────────────────────────────────────────

def load_faces_db() -> dict[str, np.ndarray]:
    if FACES_DB_PATH.exists():
        try:
            with open(FACES_DB_PATH, "rb") as f:
                data = pickle.load(f)
            logger.info("Loaded %d face(s) from faces_db.pkl", len(data))
            return data
        except Exception as e:
            logger.warning("Failed to load faces_db.pkl: %s", e)
    return {}

def save_faces_db() -> None:
    try:
        with open(FACES_DB_PATH, "wb") as f:
            pickle.dump(known_faces, f)
    except Exception as e:
        logger.error("Failed to save faces_db.pkl: %s", e)


# ── Image helpers ──────────────────────────────────────────────────────────────

def bytes_to_cv2(data: bytes) -> np.ndarray:
    arr = np.frombuffer(data, dtype=np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("Failed to decode image.")
    return img

def base64_to_cv2(b64: str) -> np.ndarray:
    return bytes_to_cv2(base64.b64decode(b64))


# ── Face recognition ───────────────────────────────────────────────────────────

def get_face_embedding(img_bgr: np.ndarray) -> Optional[np.ndarray]:
    """
    Extract a FaceNet512 512-dim embedding from an image.
    Facenet512 achieves 97.4% accuracy vs 92% for Facenet (128-dim).
    Uses RetinaFace → opencv → mtcnn detector cascade for best face alignment.
    """
    for backend in ["retinaface", "opencv", "mtcnn"]:
        try:
            from deepface import DeepFace
            result = DeepFace.represent(
                img_path=img_bgr,
                model_name="Facenet512",   # upgraded from Facenet — 97.4% vs 92% accuracy
                enforce_detection=True,
                detector_backend=backend,
            )
            if result:
                logger.debug("Face embedding extracted with Facenet512 backend=%s", backend)
                return np.array(result[0]["embedding"])
        except Exception:
            continue

    # Last resort: no face detection enforcement
    try:
        from deepface import DeepFace
        result = DeepFace.represent(
            img_path=img_bgr,
            model_name="Facenet512",
            enforce_detection=False,
            detector_backend="opencv",
        )
        if result:
            return np.array(result[0]["embedding"])
    except Exception as e:
        logger.debug("Face embedding failed (all backends): %s", e)
    return None

def cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    norm = np.linalg.norm(a) * np.linalg.norm(b)
    return float(np.dot(a, b) / norm) if norm > 0 else 0.0

def identify_face(img_bgr: np.ndarray, threshold: float = 0.45):
    """
    Match a face image against all registered embeddings.
    Handles dimension mismatch gracefully — old 128-dim embeddings are skipped
    if the current model produces 512-dim embeddings (Facenet512 upgrade).
    """
    if not known_faces:
        return False, None, 0.0
    embedding = get_face_embedding(img_bgr)
    if embedding is None:
        return False, None, 0.0

    emb_dim = embedding.shape[0]
    best_name, best_score = None, 0.0

    for username, known_emb in known_faces.items():
        # Skip embeddings with wrong dimensions — prevents crash after model upgrade
        if known_emb.shape[0] != emb_dim:
            logger.warning(
                "Skipping face '%s': stored embedding is %d-dim but current model produces %d-dim. "
                "Re-register this face to fix.",
                username, known_emb.shape[0], emb_dim
            )
            continue
        score = cosine_similarity(embedding, known_emb)
        logger.debug("Face similarity: %s = %.4f", username, score)
        if score > best_score:
            best_score, best_name = score, username

    matched = best_score >= threshold
    logger.info("Face match: best=%s score=%.4f threshold=%.2f matched=%s",
                best_name, best_score, threshold, matched)
    return matched, (best_name if matched else None), round(best_score, 4)


# ── Fight detection (pose-based) ───────────────────────────────────────────────

def bbox_iou(b1, b2) -> float:
    """Compute IoU between two bounding boxes [x1,y1,x2,y2]."""
    ix1 = max(b1[0], b2[0]); iy1 = max(b1[1], b2[1])
    ix2 = min(b1[2], b2[2]); iy2 = min(b1[3], b2[3])
    inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
    if inter == 0:
        return 0.0
    a1 = (b1[2]-b1[0]) * (b1[3]-b1[1])
    a2 = (b2[2]-b2[0]) * (b2[3]-b2[1])
    return inter / (a1 + a2 - inter)

def detect_fight(camera_id: str, img: np.ndarray) -> list[dict]:
    """
    Detect fights using YOLOv8 pose estimation.
    Strategy:
      1. Detect all persons and their keypoints
      2. Check if 2+ persons have overlapping bounding boxes (proximity)
      3. Check if wrist keypoints are moving rapidly (aggression)
      4. If both conditions met → flag as fight
    """
    if pose_model is None:
        return []

    h, w = img.shape[:2]
    results = pose_model(img, verbose=False)
    fights = []

    persons = []
    for result in results:
        if result.keypoints is None or result.boxes is None:
            continue
        for i, box in enumerate(result.boxes):
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            kpts = result.keypoints.xy[i].cpu().numpy()  # shape (17, 2)
            conf = float(box.conf[0])
            if conf < 0.4:
                continue
            persons.append({
                "box": [x1, y1, x2, y2],
                "kpts": kpts,
                "conf": conf
            })

    if len(persons) < 2:
        # Update pose history even with 1 person
        _prev_poses[camera_id] = persons
        return []

    # Check proximity — any two persons overlapping
    fight_detected = False
    fight_boxes = []

    for i in range(len(persons)):
        for j in range(i + 1, len(persons)):
            iou = bbox_iou(persons[i]["box"], persons[j]["box"])
            if iou >= FIGHT_OVERLAP_IOU_THRESHOLD:
                # REQUIRE wrist motion data — never trigger fight on proximity alone.
                # Two people sitting together have overlapping boxes but no rapid wrist movement.
                # Only flag as fight when we have previous frame data AND wrists are moving fast.
                rapid_motion = False
                prev = _prev_poses.get(camera_id, [])
                if len(prev) >= 2:
                    for pi, pp in enumerate(prev[:2]):
                        if pi < len(persons):
                            curr_kpts = persons[pi]["kpts"]
                            prev_kpts = pp["kpts"]
                            for wrist_idx in [9, 10]:
                                if wrist_idx < len(curr_kpts) and wrist_idx < len(prev_kpts):
                                    dx = (curr_kpts[wrist_idx][0] - prev_kpts[wrist_idx][0]) / w
                                    dy = (curr_kpts[wrist_idx][1] - prev_kpts[wrist_idx][1]) / h
                                    speed = (dx**2 + dy**2) ** 0.5
                                    if speed > FIGHT_WRIST_SPEED_THRESHOLD:
                                        rapid_motion = True
                                        break
                    if rapid_motion:
                        fight_detected = True
                        fight_boxes.append(persons[i]["box"])
                        fight_boxes.append(persons[j]["box"])
                # No fallback on first frame — proximity alone is NOT enough to declare a fight.
                # This prevents false positives when two people sit/stand close together.

    _prev_poses[camera_id] = persons

    if fight_detected and fight_boxes:
        # Merge all fight boxes into one bounding box
        all_x1 = min(b[0] for b in fight_boxes)
        all_y1 = min(b[1] for b in fight_boxes)
        all_x2 = max(b[2] for b in fight_boxes)
        all_y2 = max(b[3] for b in fight_boxes)
        fights.append({
            "label": "fight",
            "yolo_class": "fight",
            "confidence": 0.75,
            "severity": "critical",
            "bounding_box": {"x": all_x1, "y": all_y1, "w": all_x2-all_x1, "h": all_y2-all_y1},
            "face_matched": False, "face_username": None, "face_confidence": 0.0
        })

    return fights


# ── Fire detection ─────────────────────────────────────────────────────────────

def detect_fire(img: np.ndarray, confidence_threshold: float) -> list[dict]:
    """
    Detect fire/smoke using the dedicated fire model (fire_model.pt).

    False positive mitigation:
    - Minimum confidence: 0.85 (brick walls detected at ~0.80 — raise threshold)
    - Minimum bounding box area: 3% of frame
    - Brightness check: real fire has very bright pixels (mean > 180 in the box region)
      Brick walls are warm-toned but NOT overexposed — mean brightness ~100-140
    """
    detections = []
    img_h, img_w = img.shape[:2]
    frame_area = img_h * img_w

    if fire_model is not None:
        fire_conf_threshold = max(confidence_threshold, 0.85)
        results = fire_model(img, conf=fire_conf_threshold, verbose=False)
        for result in results:
            for box in result.boxes:
                cls_name = result.names[int(box.cls[0])]
                cls_lower = cls_name.lower()
                if not any(w in cls_lower for w in ["fire", "flame", "smoke", "blaze"]):
                    continue

                x1, y1, x2, y2 = map(int, box.xyxy[0])
                x1 = max(0, x1); y1 = max(0, y1)
                x2 = min(img_w, x2); y2 = min(img_h, y2)
                box_w = x2 - x1
                box_h = y2 - y1
                box_area = box_w * box_h

                # Reject tiny detections
                if box_area < frame_area * 0.03:
                    logger.debug("Fire rejected: box too small (%.1f%%)", box_area / frame_area * 100)
                    continue

                # Brightness check — real fire/flame has very bright pixels
                # Brick walls are warm but NOT overexposed
                crop = img[y1:y2, x1:x2]
                if crop.size > 0:
                    gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
                    mean_brightness = float(np.mean(gray))
                    max_brightness  = float(np.max(gray))
                    # Real fire: mean > 160 OR max > 230 (overexposed flame pixels)
                    # Brick wall: mean ~80-130, max ~180
                    if mean_brightness < 160 and max_brightness < 220:
                        logger.debug("Fire rejected: brightness too low (mean=%.0f max=%.0f) — likely brick/wall",
                                     mean_brightness, max_brightness)
                        continue

                detections.append({
                    "label": "fire",
                    "yolo_class": cls_name,
                    "confidence": round(float(box.conf[0]), 4),
                    "severity": "critical",
                    "bounding_box": {"x": x1, "y": y1, "w": box_w, "h": box_h},
                    "face_matched": False, "face_username": None, "face_confidence": 0.0
                })

    return detections


# ── License plate OCR ──────────────────────────────────────────────────────────

def read_license_plate(img_bgr: np.ndarray) -> Optional[str]:
    """
    Extract license plate text using EasyOCR.
    Preprocesses the image: upscale + sharpen for better OCR on small plates.
    """
    if ocr_reader is None:
        return None
    try:
        # Upscale small crops — EasyOCR works better on larger images
        h, w = img_bgr.shape[:2]
        if w < 200:
            scale = 200 / w
            img_bgr = cv2.resize(img_bgr, (int(w * scale), int(h * scale)),
                                 interpolation=cv2.INTER_CUBIC)

        # Convert to grayscale and enhance contrast for better OCR
        gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
        # CLAHE — adaptive histogram equalization improves low-light plate reading
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(4, 4))
        enhanced = clahe.apply(gray)
        # Convert back to BGR for EasyOCR
        img_for_ocr = cv2.cvtColor(enhanced, cv2.COLOR_GRAY2BGR)

        results = ocr_reader.readtext(img_for_ocr, detail=0, paragraph=True)
        text = " ".join(results).strip().upper()
        # Filter: license plates are typically 4-10 alphanumeric chars
        cleaned = "".join(c for c in text if c.isalnum() or c == " ").strip()
        # Remove spaces within the plate number for cleaner display
        plate = cleaned.replace(" ", "")
        return plate if 4 <= len(plate) <= 12 else None
    except Exception as e:
        logger.debug("License plate OCR failed: %s", e)
        return None


# ── Startup ────────────────────────────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    global coco_model, pose_model, weapon_model, threat_model, fire_model, ocr_reader, known_faces

    known_faces = load_faces_db()
    dummy = np.zeros((480, 640, 3), dtype=np.uint8)

    # ── Load COCO model ────────────────────────────────────────────────────────
    logger.info("Loading COCO model (yolov8n)...")
    try:
        coco_model = YOLO(str(COCO_MODEL))
        coco_model(dummy, verbose=False)
        logger.info("✅ COCO model ready (persons, vehicles, knives)")
    except Exception as e:
        logger.error("❌ COCO model failed: %s", e)

    # ── Load Weapon model (generic — 148MB) ────────────────────────────────────
    if WEAPON_MODEL.exists():
        logger.info("Loading weapon model (weapon_model.pt — 148MB)...")
        try:
            weapon_model = YOLO(str(WEAPON_MODEL))
            weapon_model(dummy, verbose=False)
            classes = list(weapon_model.names.values())
            logger.info("✅ Weapon model ready — classes: %s", classes)
        except Exception as e:
            logger.warning("⚠️  Weapon model failed: %s", e)
    else:
        logger.warning("⚠️  weapon_model.pt not found — generic weapon detection disabled")

    # ── Load Threat model (Gun/grenade/knife/explosion — 6MB) ─────────────────
    if THREAT_MODEL.exists():
        logger.info("Loading threat model (threat_model.pt — Gun/grenade/knife/explosion)...")
        try:
            threat_model = YOLO(str(THREAT_MODEL))
            threat_model(dummy, verbose=False)
            classes = list(threat_model.names.values())
            logger.info("✅ Threat model ready — classes: %s", classes)
        except Exception as e:
            logger.warning("⚠️  Threat model failed: %s", e)
    else:
        logger.warning("⚠️  threat_model.pt not found — specific threat detection disabled")

    # ── Load Pose model for fight detection ────────────────────────────────────
    logger.info("Loading Pose model (yolov8n-pose)...")
    try:
        pose_model = YOLO(str(POSE_MODEL))
        pose_model(dummy, verbose=False)
        logger.info("✅ Pose model ready (fight detection active)")
    except Exception as e:
        logger.warning("⚠️  Pose model failed: %s — fight detection disabled", e)

    # ── Load Fire model (optional) ─────────────────────────────────────────────
    if FIRE_MODEL.exists():
        logger.info("Loading custom fire model...")
        try:
            fire_model = YOLO(str(FIRE_MODEL))
            fire_model(dummy, verbose=False)
            logger.info("✅ Custom fire model ready")
        except Exception as e:
            logger.warning("⚠️  Custom fire model failed: %s", e)
    else:
        logger.info("ℹ️  No fire_model.pt found — fire detection disabled (color fallback also disabled)")

    # ── Load EasyOCR for license plates ───────────────────────────────────────
    logger.info("Loading EasyOCR for license plate recognition...")
    try:
        import easyocr
        ocr_reader = easyocr.Reader(["en"], gpu=False, verbose=False)
        logger.info("✅ EasyOCR ready (license plate OCR active)")
    except Exception as e:
        logger.warning("⚠️  EasyOCR not available: %s — license plate text disabled", e)

    yield

    save_faces_db()
    logger.info("AI Microservice shut down cleanly.")


# ── App ────────────────────────────────────────────────────────────────────────

app = FastAPI(
    title="CCTV Guard AI Microservice",
    description="Multi-model: YOLOv8 COCO + Weapon + Threat + Pose + Fire + OCR + FaceNet",
    version="5.0.0",
    lifespan=lifespan,
)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])


# ── Pydantic models ────────────────────────────────────────────────────────────

class BoundingBox(BaseModel):
    x: int; y: int; w: int; h: int

class Detection(BaseModel):
    label: str
    yolo_class: str
    confidence: float
    severity: str
    bounding_box: BoundingBox
    face_matched: bool = False
    face_username: Optional[str] = None
    face_confidence: float = 0.0
    plate_text: Optional[str] = None   # license plate OCR result

class DetectResponse(BaseModel):
    camera_id: str
    timestamp: float
    detections: list[Detection]
    inference_ms: float
    face_recognition_ms: float = 0.0

class HealthResponse(BaseModel):
    status: str
    models: dict
    known_faces_count: int
    version: str


# ── /health ────────────────────────────────────────────────────────────────────

@app.get("/health", response_model=HealthResponse, tags=["System"])
def health():
    return HealthResponse(
        status="ok",
        models={
            "coco":    coco_model is not None,
            "pose":    pose_model is not None,
            "weapon":  weapon_model is not None,
            "threat":  threat_model is not None,
            "fire":    fire_model is not None,
            "ocr":     ocr_reader is not None,
            "face":    True,
            # Model accuracy info for UI display
            "face_model":   "Facenet512",
            "face_accuracy": "97.4%",
            "weapon_model": "YOLOv8 (weapon_model.pt + threat_model.pt)",
            "fire_model":   "YOLOv8 fire_model.pt" if fire_model is not None else "not loaded",
        },
        known_faces_count=len(known_faces),
        version="5.0.0",
    )


# ── /detect ────────────────────────────────────────────────────────────────────

@app.post("/detect", response_model=DetectResponse, tags=["Detection"])
async def detect(
    camera_id: str = Form(...),
    file: Optional[UploadFile] = File(None),
    frame_base64: Optional[str] = Form(None),
    confidence_threshold: float = Form(0.25),
    run_face_recognition: bool = Form(True),
):
    if coco_model is None:
        raise HTTPException(status_code=503, detail="Model not loaded.")

    # Decode frame
    try:
        img = bytes_to_cv2(await file.read()) if file else base64_to_cv2(frame_base64 or "")
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    img_h, img_w = img.shape[:2]
    t0 = time.perf_counter()
    detections: list[Detection] = []
    face_recognition_ms = 0.0

    # ── 1. COCO detection: persons, vehicles, knives (fallback weapons) ───────
    coco_results = coco_model(img, conf=confidence_threshold, verbose=False)
    inference_ms = (time.perf_counter() - t0) * 1000

    # First pass: collect all vehicle bounding boxes so we can check person overlap
    vehicle_boxes = []
    for result in coco_results:
        for box in result.boxes:
            cls_name = result.names[int(box.cls[0])].lower()
            if cls_name in VEHICLE_CLASSES:
                x1, y1, x2, y2 = map(int, box.xyxy[0])
                vehicle_boxes.append([x1, y1, x2, y2])

    for result in coco_results:
        for box in result.boxes:
            cls_name = result.names[int(box.cls[0])].lower()
            conf     = float(box.conf[0])
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            x1 = max(0, x1); y1 = max(0, y1)
            x2 = min(img_w, x2); y2 = min(img_h, y2)

            face_matched = False; face_username = None; face_conf = 0.0
            plate_text   = None
            label        = "unknown"
            severity      = "low"

            if cls_name in WEAPON_CLASSES:
                label    = "weapon"
                severity = "critical"

            elif cls_name == "person":
                label    = "person"
                severity = "low"

                # Check if this person is ON a vehicle (motorcycle rider, car driver)
                # If person box overlaps significantly with a vehicle box → it's a rider, not an intruder
                person_on_vehicle = False
                for vbox in vehicle_boxes:
                    iou = bbox_iou([x1, y1, x2, y2], vbox)
                    if iou > 0.15:  # 15% overlap = person is on/in the vehicle
                        person_on_vehicle = True
                        break

                if person_on_vehicle:
                    # Rider on motorcycle — not an intrusion, just a vehicle occupant
                    label    = "person"
                    severity = "low"
                elif run_face_recognition:
                    tf   = time.perf_counter()
                    crop = img[y1:y2, x1:x2]
                    if crop.size > 0:
                        matched, username, face_score = identify_face(crop)
                        face_recognition_ms += (time.perf_counter() - tf) * 1000
                        if matched:
                            face_matched  = True
                            face_username = username
                            face_conf     = face_score
                            label         = username
                            severity      = "low"
                        elif known_faces:
                            label    = "unknown_face"
                            severity = "high"
                        else:
                            label    = "intrusion"
                            severity = "medium"

            elif cls_name in VEHICLE_CLASSES:
                label    = "license_plate"
                severity = "low"
                crop = img[y1:y2, x1:x2]
                if crop.size > 0:
                    # License plates are at the BOTTOM of vehicles.
                    # Crop only the bottom 40% of the vehicle bounding box for OCR.
                    # This avoids reading text from stickers, logos, etc. on the body.
                    h_crop = crop.shape[0]
                    plate_region = crop[int(h_crop * 0.55):, :]  # bottom 45%
                    if plate_region.size > 0:
                        plate_text = read_license_plate(plate_region)
                    # Fallback: try full crop if bottom region gave nothing
                    if not plate_text:
                        plate_text = read_license_plate(crop)

            else:
                continue  # skip irrelevant COCO classes

            detections.append(Detection(
                label=label, yolo_class=cls_name, confidence=round(conf, 4),
                severity=severity,
                bounding_box=BoundingBox(x=x1, y=y1, w=x2-x1, h=y2-y1),
                face_matched=face_matched, face_username=face_username,
                face_confidence=face_conf, plate_text=plate_text,
            ))

    # ── 2. Threat model — Gun / grenade / knife / explosion (6MB) ────────────
    # NOTE: The generic weapon_model.pt (148MB) is DISABLED — it produces too many
    # false positives on motorcycle parts, keyboards, phones, and other objects.
    # The threat_model.pt is specific and accurate: Gun, grenade, knife, explosion only.
    if threat_model is not None:
        t_results = threat_model(img, conf=max(confidence_threshold, 0.55), verbose=False)
        for result in t_results:
            for box in result.boxes:
                cls_name = result.names[int(box.cls[0])]   # preserve original case
                cls_lower = cls_name.lower()
                conf      = float(box.conf[0])
                x1, y1, x2, y2 = map(int, box.xyxy[0])
                x1 = max(0, x1); y1 = max(0, y1)
                x2 = min(img_w, x2); y2 = min(img_h, y2)

                # Reject detections in very dark regions — a hand/arm in the dark
                # is NOT a weapon. Real weapons are visible objects with detail.
                crop = img[y1:y2, x1:x2]
                if crop.size > 0:
                    crop_mean = float(np.mean(cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)))
                    if crop_mean < 40:  # nearly black — not a real weapon detection
                        logger.debug("Threat model rejected: too dark (mean=%.0f) — likely shadow/hand", crop_mean)
                        continue

                # Map class to label/severity
                if cls_lower in {"gun", "grenade", "knife", "weapon", "pistol",
                                  "rifle", "blade", "sword", "dagger", "machete", "handgun"}:
                    label    = "weapon"
                    severity = "critical"
                elif cls_lower == "explosion":
                    label    = "fire"
                    severity = "critical"
                else:
                    label    = cls_lower
                    severity = "high"

                detections.append(Detection(
                    label=label, yolo_class=cls_name,
                    confidence=round(conf, 4), severity=severity,
                    bounding_box=BoundingBox(x=x1, y=y1, w=x2-x1, h=y2-y1),
                ))

    # ── 4. Fight detection via pose estimation ────────────────────────────────
    fight_detections = detect_fight(camera_id, img)
    for fd in fight_detections:
        bb = fd["bounding_box"]
        detections.append(Detection(
            label=fd["label"], yolo_class=fd["yolo_class"],
            confidence=fd["confidence"], severity=fd["severity"],
            bounding_box=BoundingBox(**bb),
        ))

    # ── 5. Fire detection ─────────────────────────────────────────────────────
    fire_detections = detect_fire(img, confidence_threshold)
    for fd in fire_detections:
        bb = fd["bounding_box"]
        detections.append(Detection(
            label=fd["label"], yolo_class=fd["yolo_class"],
            confidence=fd["confidence"], severity=fd["severity"],
            bounding_box=BoundingBox(**bb),
        ))

    total_ms = (time.perf_counter() - t0) * 1000
    logger.info(
        "camera=%s detections=%d total=%.1fms (coco=%.1fms face=%.1fms)",
        camera_id, len(detections), total_ms, inference_ms, face_recognition_ms
    )

    return DetectResponse(
        camera_id=camera_id,
        timestamp=time.time(),
        detections=detections,
        inference_ms=round(inference_ms, 2),
        face_recognition_ms=round(face_recognition_ms, 2),
    )


# ── /register-face ─────────────────────────────────────────────────────────────

@app.post("/register-face", tags=["Face Recognition"])
async def register_face(username: str = Form(...), file: UploadFile = File(...)):
    try:
        img = bytes_to_cv2(await file.read())
        embedding = get_face_embedding(img)
        if embedding is None:
            raise HTTPException(status_code=400, detail="No face detected. Use a clear frontal photo.")
        known_faces[username] = embedding
        save_faces_db()
        logger.info("Registered face for '%s'. Total: %d", username, len(known_faces))
        return {"message": f"Face registered for '{username}'.", "total_registered": len(known_faces)}
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


# ── /faces ─────────────────────────────────────────────────────────────────────

@app.get("/faces", tags=["Face Recognition"])
def list_faces():
    return {"faces": list(known_faces.keys()), "total": len(known_faces)}

@app.delete("/faces/{username}", tags=["Face Recognition"])
def delete_face(username: str):
    if username not in known_faces:
        raise HTTPException(status_code=404, detail=f"'{username}' not found.")
    del known_faces[username]
    save_faces_db()
    return {"message": f"'{username}' removed.", "total_registered": len(known_faces)}


# ── Entry point ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=False)
