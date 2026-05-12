export interface User {
  id: string;
  username: string;
  email: string;
  role: 'Admin' | 'Operator' | 'Viewer';
  token?: string;
}

export interface Camera {
  id: string;
  name: string;
  location: string;
  ipAddress: string;
  port: number;
  status: 'online' | 'offline' | 'error';
  streamUrl?: string;
  rtspUrl?: string;
  detectionEnabled: boolean;
  confidenceThreshold: number;
  frameRate: number;
  lastSeen?: Date;
}

export interface Incident {
  id: string;
  cameraId: string;
  cameraName: string;
  type: 'fight' | 'weapon' | 'intrusion' | 'unknown_face' | 'license_plate';
  severity: 'critical' | 'high' | 'medium' | 'low';
  confidence: number;
  timestamp: Date;
  thumbnailUrl?: string;
  boundingBox?: { x: number; y: number; width: number; height: number };
  status: 'new' | 'acknowledged' | 'resolved';
  notes?: string;
}

export interface Alert {
  id: string;
  incidentId: string;
  type: string;
  message: string;
  cameraName: string;
  severity: 'critical' | 'high' | 'medium' | 'low';
  timestamp: Date;
  read: boolean;
  dismissed: boolean;
  isEscalated?: boolean;
  imageUrl?: string;
}

export interface EmergencyNotification {
  alertId: string;
  incidentId: string;
  type: string;
  message: string;
  cameraName: string;
  severity: string;
  timestamp: Date;
  imageUrl?: string;
  escalatedBy: string;
  escalatedAt: Date;
}

export interface SystemStats {
  totalCameras: number;
  onlineCameras: number;
  todayIncidents: number;
  activeAlerts: number;
  systemUptime: string;
  avgLatency: number;
  detectionAccuracy: number;
}

export interface ManagedUser {
  id: string;
  username: string;
  email: string;
  role: 'Admin' | 'Operator' | 'Viewer';
  status: 'active' | 'suspended';
  createdAt: Date;
  lastLogin: Date | null;
}

export interface LoginCredentials {
  username: string;
  password: string;
}

// ── Analytics models ──────────────────────────────────────────────────────────

export interface UserSession {
  userId: string;
  username: string;
  role: 'Admin' | 'Operator' | 'Viewer';
  date: Date;
  hoursActive: number;
  month: number;   // 1-12
  year: number;
}

export interface CameraDetectionStat {
  cameraId: string;
  cameraName: string;
  totalDetections: number;
  fightCount: number;
  weaponCount: number;
  intrusionCount: number;
  unknownFaceCount: number;
  licensePlateCount: number;
}

export interface MonthlyAlertStat {
  month: number;   // 1-12
  year: number;
  label: string;   // e.g. "Jan 2025"
  total: number;
  critical: number;
  high: number;
  medium: number;
  low: number;
}

export interface CameraAlertStat {
  cameraId: string;
  cameraName: string;
  total: number;
  critical: number;
  high: number;
}

export interface AnalyticsData {
  userSessions: UserSession[];
  cameraDetectionStats: CameraDetectionStat[];
  monthlyAlertStats: MonthlyAlertStat[];
  cameraAlertStats: CameraAlertStat[];
}
