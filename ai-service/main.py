"""
CCTV Guard — AI Microservice v8.0
Parallel multi-model detection: weapon, fire, person/face, vehicle/plate, fight
All models run concurrently using ThreadPoolExecutor for maximum speed.
"""

import base64
import logging
import pickle
import time
import concurrent.futures
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Optional

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from ultralytics import YOLO

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger(__name__)

BASE_DIR      = Path(__file__).parent
FACES_DB_PATH = BASE_DIR / "faces_db.pkl"
COCO_MODEL    = BASE_DIR / "yolov8n.pt"
POSE_MODEL    = BASE_DIR / "yolov8n-pose.pt"
WEAPON_MODEL  = BASE_DIR / "weapon_model.pt"
THREAT_MODEL  = BASE_DIR / "threat_model.pt"
FIRE_MODEL    = BASE_DIR / "fire_model.pt"

coco_model:   Optional[YOLO] = None
pose_model:   Optional[YOLO] = None
weapon_model: Optional[YOLO] = None
threat_model: Optional[YOLO] = None
fire_model:   Optional[YOLO] = None
ocr_reader    = None
known_faces: dict = {}

# Thread pool for parallel model inference
_executor = concurrent.futures.ThreadPoolExecutor(max_workers=5)

# COCO classes we care about
WEAPON_CLASSES  = {"knife", "scissors", "baseball bat"}
VEHICLE_CLASSES = {"car", "truck", "motorcycle", "bus", "bicycle"}

# ── Face DB ───────────────────────────────────────────────────────────────────

def load_faces_db() -> dict:
    if FACES_DB_PATH.exists():
        try:
            with open(FACES_DB_PATH, "rb") as f:
                data = pickle.load(f)
            logger.info("Loaded %d face(s)", len(data))
            return data
        except Exception as e:
            logger.warning("faces_db load failed: %s", e)
    return {}

def save_faces_db():
    try:
        with open(FACES_DB_PATH, "wb") as f:
            pickle.dump(known_faces, f)
    except Exception as e:
        logger.error("faces_db save failed: %s", e)

# ── Image helpers ─────────────────────────────────────────────────────────────

def bytes_to_cv2(data: bytes) -> np.ndarray:
    arr = np.frombuffer(data, dtype=np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("Failed to decode image")
    return img

def base64_to_cv2(b64: str) -> np.ndarray:
    return bytes_to_cv2(base64.b64decode(b64))

# ── Face recognition ──────────────────────────────────────────────────────────

def cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    norm = np.linalg.norm(a) * np.linalg.norm(b)
    return float(np.dot(a, b) / norm) if norm > 0 else 0.0

def get_face_embedding(img_bgr: np.ndarray) -> Optional[np.ndarray]:
    for backend in ["opencv", "mtcnn"]:
        try:
            from deepface import DeepFace
            result = DeepFace.represent(
                img_path=img_bgr,
                model_name="Facenet512",
                enforce_detection=True,
                detector_backend=backend,
            )
            if result:
                return np.array(result[0]["embedding"])
        except Exception:
            continue
    return None

def identify_face(img_bgr: np.ndarray, threshold: float = 0.70):
    """Match face against registered embeddings. Threshold: 0.70."""
    if not known_faces:
        return False, None, 0.0
    embedding = get_face_embedding(img_bgr)
    if embedding is None:
        return False, None, 0.0
    emb_dim = embedding.shape[0]
    best_name, best_score = None, 0.0
    for username, known_emb in known_faces.items():
        if known_emb.shape[0] != emb_dim:
            continue
        score = cosine_similarity(embedding, known_emb)
        if score > best_score:
            best_score, best_name = score, username
    matched = best_score >= threshold
    return matched, (best_name if matched else None), round(best_score, 4)

# ── IoU ───────────────────────────────────────────────────────────────────────

def bbox_iou(b1, b2) -> float:
    ix1 = max(b1[0], b2[0]); iy1 = max(b1[1], b2[1])
    ix2 = min(b1[2], b2[2]); iy2 = min(b1[3], b2[3])
    inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
    if inter == 0:
        return 0.0
    a1 = (b1[2]-b1[0]) * (b1[3]-b1[1])
    a2 = (b2[2]-b2[0]) * (b2[3]-b2[1])
    return inter / (a1 + a2 - inter)

# ── Deduplication ─────────────────────────────────────────────────────────────

def _deduplicate(detections: list) -> list:
    """Keep highest-confidence box when multiple models detect the same object."""
    if len(detections) <= 1:
        return detections
    sorted_dets = sorted(detections, key=lambda d: d.confidence, reverse=True)
    kept = []
    for det in sorted_dets:
        b1 = [det.bounding_box.x, det.bounding_box.y,
              det.bounding_box.x + det.bounding_box.w,
              det.bounding_box.y + det.bounding_box.h]
        dup = False
        for k in kept:
            if k.label != det.label:
                continue
            b2 = [k.bounding_box.x, k.bounding_box.y,
                  k.bounding_box.x + k.bounding_box.w,
                  k.bounding_box.y + k.bounding_box.h]
            if bbox_iou(b1, b2) > 0.40:
                dup = True; break
        if not dup:
            kept.append(det)
    return kept

# ── Model inference functions (run in thread pool) ────────────────────────────

def _run_coco(img: np.ndarray, conf: float, run_face: bool) -> list:
    """COCO: persons, vehicles, knives."""
    if coco_model is None:
        return []
    results_list = []
    img_h, img_w = img.shape[:2]
    results = coco_model(img, conf=conf, verbose=False)

    vehicle_boxes = []
    for r in results:
        for box in r.boxes:
            if r.names[int(box.cls[0])].lower() in VEHICLE_CLASSES:
                vehicle_boxes.append(list(map(int, box.xyxy[0])))

    for r in results:
        for box in r.boxes:
            cls  = r.names[int(box.cls[0])].lower()
            c    = float(box.conf[0])
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            x1 = max(0, x1); y1 = max(0, y1)
            x2 = min(img_w, x2); y2 = min(img_h, y2)
            if x2 <= x1 or y2 <= y1:
                continue

            label = None; severity = "low"
            face_matched = False; face_username = None; face_conf_val = 0.0
            plate_text = None

            if cls in WEAPON_CLASSES:
                label = "weapon"; severity = "critical"
                logger.info("COCO weapon: %s %.0f%%", cls, c*100)

            elif cls == "person":
                on_vehicle = any(bbox_iou([x1,y1,x2,y2], vb) > 0.15 for vb in vehicle_boxes)
                if on_vehicle:
                    label = "person"; severity = "low"
                elif known_faces and run_face:
                    crop = img[y1:y2, x1:x2]
                    if crop.size > 0 and (x2-x1) >= 60 and (y2-y1) >= 60:
                        matched, uname, fscore = identify_face(crop)
                        if matched:
                            face_matched = True; face_username = uname
                            face_conf_val = fscore; label = uname; severity = "low"
                        else:
                            label = "unknown_face"; severity = "high"
                    else:
                        label = "intrusion"; severity = "medium"
                else:
                    label = "intrusion"; severity = "medium"
                logger.info("Person: %s %.0f%%", label, c*100)

            elif cls in VEHICLE_CLASSES:
                label = "license_plate"; severity = "low"
                crop = img[y1:y2, x1:x2]
                if crop.size > 0:
                    h_crop = crop.shape[0]
                    bottom = crop[int(h_crop * 0.55):, :]
                    plate_text = (read_license_plate(bottom) if bottom.size > 0 else None) \
                                 or read_license_plate(crop)
                logger.info("Vehicle: %s %.0f%% plate=%s", cls, c*100, plate_text)

            if label is None:
                continue

            results_list.append(Detection(
                label=label, yolo_class=cls, confidence=round(c, 4),
                severity=severity,
                bounding_box=BoundingBox(x=x1, y=y1, w=x2-x1, h=y2-y1),
                face_matched=face_matched, face_username=face_username,
                face_confidence=face_conf_val, plate_text=plate_text,
            ))
    return results_list


def _run_threat(img: np.ndarray, conf: float) -> list:
    """Threat model: knife, gun, grenade."""
    if threat_model is None:
        return []
    results_list = []
    img_h, img_w = img.shape[:2]
    for r in threat_model(img, conf=max(conf, 0.30), verbose=False):
        for box in r.boxes:
            cls  = r.names[int(box.cls[0])]
            c    = float(box.conf[0])
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            x1 = max(0, x1); y1 = max(0, y1)
            x2 = min(img_w, x2); y2 = min(img_h, y2)
            if x2 <= x1 or y2 <= y1:
                continue
            crop = img[y1:y2, x1:x2]
            if crop.size > 0 and float(np.mean(cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY))) < 10:
                continue
            cls_lower = cls.lower()
            if cls_lower in {"gun","grenade","knife","weapon","pistol","rifle",
                              "blade","sword","dagger","machete","handgun"}:
                label = "weapon"; sev = "critical"
            elif cls_lower == "explosion":
                label = "fire"; sev = "critical"
            else:
                label = cls_lower; sev = "high"
            logger.info("Threat: %s %.0f%% → %s", cls, c*100, label)
            results_list.append(Detection(
                label=label, yolo_class=cls, confidence=round(c, 4), severity=sev,
                bounding_box=BoundingBox(x=x1, y=y1, w=x2-x1, h=y2-y1),
            ))
    return results_list


def _run_weapon(img: np.ndarray, conf: float) -> list:
    """Generic weapon model (148MB)."""
    if weapon_model is None:
        return []
    results_list = []
    img_h, img_w = img.shape[:2]
    for r in weapon_model(img, conf=max(conf, 0.35), verbose=False):
        for box in r.boxes:
            cls  = r.names[int(box.cls[0])]
            c    = float(box.conf[0])
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            x1 = max(0, x1); y1 = max(0, y1)
            x2 = min(img_w, x2); y2 = min(img_h, y2)
            if x2 <= x1 or y2 <= y1:
                continue
            logger.info("Weapon model: %s %.0f%%", cls, c*100)
            results_list.append(Detection(
                label="weapon", yolo_class=cls, confidence=round(c, 4), severity="critical",
                bounding_box=BoundingBox(x=x1, y=y1, w=x2-x1, h=y2-y1),
            ))
    return results_list


def _run_fire(img: np.ndarray, conf: float) -> list:
    """Fire/smoke model — accepts ALL classes from fire_model (no name filter)."""
    if fire_model is None:
        return []
    results_list = []
    img_h, img_w = img.shape[:2]
    frame_area = img_h * img_w

    # Use 0.45 minimum — low enough to catch real fire
    for r in fire_model(img, conf=max(conf, 0.45), verbose=False):
        for box in r.boxes:
            cls_name = r.names[int(box.cls[0])]
            c = float(box.conf[0])
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            x1 = max(0, x1); y1 = max(0, y1)
            x2 = min(img_w, x2); y2 = min(img_h, y2)
            bw, bh = x2 - x1, y2 - y1
            if bw * bh < frame_area * 0.005:  # skip < 0.5% of frame
                continue
            logger.info("Fire model: %s %.0f%%", cls_name, c*100)
            results_list.append(Detection(
                label="fire", yolo_class=cls_name, confidence=round(c, 4), severity="critical",
                bounding_box=BoundingBox(x=x1, y=y1, w=bw, h=bh),
            ))
    return results_list


def _run_fight(camera_id: str, img: np.ndarray) -> list:
    """Fight detection via pose estimation."""
    if pose_model is None:
        return []
    h, w = img.shape[:2]
    results = pose_model(img, verbose=False)
    persons = []
    for result in results:
        if result.keypoints is None or result.boxes is None:
            continue
        for i, box in enumerate(result.boxes):
            if float(box.conf[0]) < 0.4:
                continue
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            kpts = result.keypoints.xy[i].cpu().numpy()
            persons.append({"box": [x1, y1, x2, y2], "kpts": kpts})

    if len(persons) < 2:
        _prev_poses[camera_id] = persons
        return []

    fights = []
    for i in range(len(persons)):
        for j in range(i + 1, len(persons)):
            if bbox_iou(persons[i]["box"], persons[j]["box"]) < 0.20:
                continue
            prev = _prev_poses.get(camera_id, [])
            if len(prev) < 2:
                continue
            rapid = False
            for pi in range(min(2, len(prev), len(persons))):
                for wrist in [9, 10]:
                    if wrist < len(persons[pi]["kpts"]) and wrist < len(prev[pi]["kpts"]):
                        dx = (persons[pi]["kpts"][wrist][0] - prev[pi]["kpts"][wrist][0]) / w
                        dy = (persons[pi]["kpts"][wrist][1] - prev[pi]["kpts"][wrist][1]) / h
                        if (dx**2 + dy**2) ** 0.5 > 0.18:
                            rapid = True; break
            if rapid:
                boxes = [persons[i]["box"], persons[j]["box"]]
                fights.append(Detection(
                    label="fight", yolo_class="fight", confidence=0.75, severity="critical",
                    bounding_box=BoundingBox(
                        x=min(b[0] for b in boxes), y=min(b[1] for b in boxes),
                        w=max(b[2] for b in boxes) - min(b[0] for b in boxes),
                        h=max(b[3] for b in boxes) - min(b[1] for b in boxes),
                    ),
                ))
    _prev_poses[camera_id] = persons
    return fights


_prev_poses: dict = {}

# ── License plate OCR ─────────────────────────────────────────────────────────

def read_license_plate(img_bgr: np.ndarray) -> Optional[str]:
    if ocr_reader is None:
        return None
    try:
        h, w = img_bgr.shape[:2]
        if w < 200 and w > 0:
            scale = 200.0 / w
            img_bgr = cv2.resize(img_bgr, (int(w*scale), int(h*scale)), interpolation=cv2.INTER_CUBIC)
        gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(4, 4))
        enhanced = cv2.cvtColor(clahe.apply(gray), cv2.COLOR_GRAY2BGR)
        results = ocr_reader.readtext(enhanced, detail=0, paragraph=True)
        plate = "".join(c for c in " ".join(results).upper() if c.isalnum())
        return plate if 4 <= len(plate) <= 12 else None
    except Exception as e:
        logger.debug("OCR failed: %s", e)
        return None

# ── Startup ───────────────────────────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    global coco_model, pose_model, weapon_model, threat_model, fire_model, ocr_reader, known_faces

    known_faces = load_faces_db()
    dummy = np.zeros((480, 640, 3), dtype=np.uint8)

    logger.info("Loading COCO model...")
    try:
        coco_model = YOLO(str(COCO_MODEL))
        coco_model(dummy, verbose=False)
        logger.info("✅ COCO ready")
    except Exception as e:
        logger.error("❌ COCO failed: %s", e)

    if THREAT_MODEL.exists():
        try:
            threat_model = YOLO(str(THREAT_MODEL))
            threat_model(dummy, verbose=False)
            logger.info("✅ Threat model ready — classes: %s", list(threat_model.names.values()))
        except Exception as e:
            logger.warning("⚠️  Threat model failed: %s", e)

    if WEAPON_MODEL.exists():
        try:
            weapon_model = YOLO(str(WEAPON_MODEL))
            weapon_model(dummy, verbose=False)
            logger.info("✅ Weapon model ready — classes: %s", list(weapon_model.names.values()))
        except Exception as e:
            logger.warning("⚠️  Weapon model failed: %s", e)

    try:
        pose_model = YOLO(str(POSE_MODEL))
        pose_model(dummy, verbose=False)
        logger.info("✅ Pose model ready")
    except Exception as e:
        logger.warning("⚠️  Pose model failed: %s", e)

    if FIRE_MODEL.exists():
        try:
            fire_model = YOLO(str(FIRE_MODEL))
            fire_model(dummy, verbose=False)
            # Log ALL class names so we know what the model outputs
            logger.info("✅ Fire model ready — ALL classes: %s", list(fire_model.names.values()))
        except Exception as e:
            logger.warning("⚠️  Fire model failed: %s", e)
    else:
        logger.warning("⚠️  fire_model.pt not found")

    try:
        import easyocr
        ocr_reader = easyocr.Reader(["en"], gpu=False, verbose=False)
        logger.info("✅ EasyOCR ready")
    except Exception as e:
        logger.warning("⚠️  EasyOCR not available: %s", e)

    logger.info("🚀 AI v8.0 ready | COCO=%s threat=%s weapon=%s pose=%s fire=%s ocr=%s",
        coco_model is not None, threat_model is not None, weapon_model is not None,
        pose_model is not None, fire_model is not None, ocr_reader is not None)
    yield
    save_faces_db()

# ── App ───────────────────────────────────────────────────────────────────────

app = FastAPI(title="CCTV Guard AI", version="8.0.0", lifespan=lifespan)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

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
    plate_text: Optional[str] = None

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

@app.get("/health", response_model=HealthResponse)
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
            "fire_classes": list(fire_model.names.values()) if fire_model else [],
        },
        known_faces_count=len(known_faces),
        version="8.0.0",
    )

@app.post("/detect", response_model=DetectResponse)
async def detect(
    camera_id: str = Form(...),
    file: Optional[UploadFile] = File(None),
    frame_base64: Optional[str] = Form(None),
    confidence_threshold: float = Form(0.25),
    run_face_recognition: bool = Form(True),
):
    if coco_model is None:
        raise HTTPException(status_code=503, detail="Models not loaded")

    try:
        img = bytes_to_cv2(await file.read()) if file else base64_to_cv2(frame_base64 or "")
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    t0 = time.perf_counter()

    # ── Run all models in PARALLEL using thread pool ──────────────────────────
    # This cuts total inference time from ~sum(all models) to ~max(slowest model)
    loop = __import__('asyncio').get_event_loop()

    futures = [
        loop.run_in_executor(_executor, _run_coco,   img, confidence_threshold, run_face_recognition),
        loop.run_in_executor(_executor, _run_threat,  img, confidence_threshold),
        loop.run_in_executor(_executor, _run_weapon,  img, confidence_threshold),
        loop.run_in_executor(_executor, _run_fire,    img, confidence_threshold),
        loop.run_in_executor(_executor, _run_fight,   camera_id, img),
    ]

    import asyncio
    results = await asyncio.gather(*futures, return_exceptions=True)

    detections: list[Detection] = []
    for r in results:
        if isinstance(r, Exception):
            logger.warning("Model error: %s", r)
        elif isinstance(r, list):
            detections.extend(r)

    # Deduplicate overlapping boxes from multiple models
    detections = _deduplicate(detections)

    inference_ms = (time.perf_counter() - t0) * 1000

    if detections:
        logger.info("camera=%s | %d det(s) %.0fms: %s",
            camera_id, len(detections), inference_ms,
            ", ".join(f"{d.label}({d.confidence:.0%})" for d in detections))

    return DetectResponse(
        camera_id=camera_id,
        timestamp=time.time(),
        detections=detections,
        inference_ms=round(inference_ms, 2),
    )

@app.post("/register-face")
async def register_face(username: str = Form(...), file: UploadFile = File(...)):
    try:
        img = bytes_to_cv2(await file.read())
        emb = get_face_embedding(img)
        if emb is None:
            raise HTTPException(status_code=400, detail="No face detected.")
        known_faces[username] = emb
        save_faces_db()
        return {"message": f"Registered '{username}'.", "total_registered": len(known_faces)}
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/faces")
def list_faces():
    return {"faces": list(known_faces.keys()), "total": len(known_faces)}

@app.delete("/faces/{username}")
def delete_face(username: str):
    if username not in known_faces:
        raise HTTPException(status_code=404, detail=f"'{username}' not found.")
    del known_faces[username]
    save_faces_db()
    return {"message": f"'{username}' removed.", "total_registered": len(known_faces)}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=False)
