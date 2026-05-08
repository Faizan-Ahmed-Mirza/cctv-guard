# CCTV Guard  Backend API Requirements

## Project Overview

**AI-Powered CCTV Guard** is a smart surveillance dashboard built with Angular 21.
This document specifies all backend API endpoints, database schema, authentication
architecture, and technology stack required to replace the current frontend mock data
with a real ASP.NET Core Web API backend.

---

## 1. Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Backend Framework | ASP.NET Core Web API | .NET 8 |
| ORM | Entity Framework Core | 8.x |
| Database | Microsoft SQL Server | 2019 / 2022 |
| Authentication | JWT Bearer Tokens |  |
| Authorization | Role-Based (Admin / Operator / Viewer) |  |
| Password Hashing | BCrypt.Net-Next | 4.x |
| Real-time | SignalR (for live alerts push) | .NET 8 |
| API Docs | Swagger / Swashbuckle | 6.x |
| CORS | Configured for Angular dev (localhost:4200) |  |

---

## 2. Authentication Architecture (JWT)

### 2.1 Token Strategy

- **Access Token**  short-lived JWT (expires in 60 minutes), sent in every request header
- **Refresh Token**  long-lived opaque token (expires in 7 days), stored in the database, used to obtain new access tokens without re-login
- **Storage (frontend)**  access token in memory (Angular service signal), refresh token in `httpOnly` cookie OR localStorage

### 2.2 JWT Claims

Every issued JWT must include the following claims:

```json
{
  "sub": "usr-001",
  "username": "admin",
  "email": "admin@cctvguard.com",
  "role": "Admin",
  "iat": 1700000000,
  "exp": 1700003600,
  "jti": "unique-token-id"
}
```

### 2.3 Token Flow

```
POST /api/auth/login
   returns { accessToken, refreshToken, user }

All protected requests:
  Authorization: Bearer <accessToken>

POST /api/auth/refresh
   body: { refreshToken }
   returns new { accessToken, refreshToken }

POST /api/auth/logout
   invalidates refresh token in DB
```

### 2.4 Role Definitions

| Role | Value | Permissions |
|---|---|---|
| Admin | `"Admin"` | Full access  all CRUD, user management, AI config, analytics |
| Operator | `"Operator"` | View feeds, acknowledge/resolve incidents, dismiss alerts, toggle camera AI detection |
| Viewer | `"Viewer"` | Read-only  view feeds, incidents, alerts. No modifications |

---

## 3. Database Schema (SQL Server / EF Core)

### 3.1 Tables

#### `Users`
```sql
Id            UNIQUEIDENTIFIER  PRIMARY KEY DEFAULT NEWID()
Username      NVARCHAR(50)      NOT NULL UNIQUE
Email         NVARCHAR(150)     NOT NULL UNIQUE
PasswordHash  NVARCHAR(255)     NOT NULL        -- BCrypt hash
Role          NVARCHAR(20)      NOT NULL        -- Admin | Operator | Viewer
Status        NVARCHAR(20)      NOT NULL DEFAULT 'active'  -- active | suspended
CreatedAt     DATETIME2         NOT NULL DEFAULT GETUTCDATE()
LastLogin     DATETIME2         NULL
```

#### `RefreshTokens`
```sql
Id            UNIQUEIDENTIFIER  PRIMARY KEY DEFAULT NEWID()
UserId        UNIQUEIDENTIFIER  NOT NULL REFERENCES Users(Id) ON DELETE CASCADE
Token         NVARCHAR(500)     NOT NULL UNIQUE
ExpiresAt     DATETIME2         NOT NULL
CreatedAt     DATETIME2         NOT NULL DEFAULT GETUTCDATE()
IsRevoked     BIT               NOT NULL DEFAULT 0
```

#### `Cameras`
```sql
Id                   NVARCHAR(50)   PRIMARY KEY   -- e.g. "cam-01"
Name                 NVARCHAR(100)  NOT NULL
Location             NVARCHAR(200)  NOT NULL
IpAddress            NVARCHAR(50)   NOT NULL
Port                 INT            NOT NULL DEFAULT 554
Status               NVARCHAR(20)   NOT NULL DEFAULT 'offline'  -- online | offline | error
StreamUrl            NVARCHAR(500)  NULL
DetectionEnabled     BIT            NOT NULL DEFAULT 1
ConfidenceThreshold  DECIMAL(4,2)   NOT NULL DEFAULT 0.85
FrameRate            INT            NOT NULL DEFAULT 30
LastSeen             DATETIME2      NULL
CreatedAt            DATETIME2      NOT NULL DEFAULT GETUTCDATE()
```

#### `Incidents`
```sql
Id              NVARCHAR(50)    PRIMARY KEY
CameraId        NVARCHAR(50)    NOT NULL REFERENCES Cameras(Id)
Type            NVARCHAR(30)    NOT NULL  -- fight | weapon | intrusion | unknown_face | license_plate
Severity        NVARCHAR(20)    NOT NULL  -- critical | high | medium | low
Confidence      DECIMAL(5,4)    NOT NULL  -- 0.0000 to 1.0000
Timestamp       DATETIME2       NOT NULL
ThumbnailUrl    NVARCHAR(500)   NULL
BoundingBoxX    INT             NULL
BoundingBoxY    INT             NULL
BoundingBoxW    INT             NULL
BoundingBoxH    INT             NULL
Status          NVARCHAR(20)    NOT NULL DEFAULT 'new'  -- new | acknowledged | resolved
Notes           NVARCHAR(1000)  NULL
AcknowledgedBy  UNIQUEIDENTIFIER NULL REFERENCES Users(Id)
AcknowledgedAt  DATETIME2       NULL
ResolvedBy      UNIQUEIDENTIFIER NULL REFERENCES Users(Id)
ResolvedAt      DATETIME2       NULL
```

#### `Alerts`
```sql
Id          NVARCHAR(50)    PRIMARY KEY
IncidentId  NVARCHAR(50)    NOT NULL REFERENCES Incidents(Id)
Type        NVARCHAR(100)   NOT NULL   -- e.g. "Fight Detected"
Message     NVARCHAR(500)   NOT NULL
CameraId    NVARCHAR(50)    NOT NULL REFERENCES Cameras(Id)
Severity    NVARCHAR(20)    NOT NULL
Timestamp   DATETIME2       NOT NULL DEFAULT GETUTCDATE()
```

#### `AlertReadStatus`  _(per-user read/dismiss state)_
```sql
Id          UNIQUEIDENTIFIER  PRIMARY KEY DEFAULT NEWID()
AlertId     NVARCHAR(50)      NOT NULL REFERENCES Alerts(Id)
UserId      UNIQUEIDENTIFIER  NOT NULL REFERENCES Users(Id)
IsRead      BIT               NOT NULL DEFAULT 0
IsDismissed BIT               NOT NULL DEFAULT 0
ReadAt      DATETIME2         NULL
DismissedAt DATETIME2         NULL
UNIQUE (AlertId, UserId)
```

#### `UserSessions`  _(for analytics  operator/viewer activity logs)_
```sql
Id          UNIQUEIDENTIFIER  PRIMARY KEY DEFAULT NEWID()
UserId      UNIQUEIDENTIFIER  NOT NULL REFERENCES Users(Id)
LoginAt     DATETIME2         NOT NULL DEFAULT GETUTCDATE()
LogoutAt    DATETIME2         NULL
DurationMin INT               NULL   -- computed on logout: (LogoutAt - LoginAt) in minutes
IpAddress   NVARCHAR(50)      NULL
UserAgent   NVARCHAR(500)     NULL
```

#### `AiSettings`  _(single-row config table)_
```sql
Id                   INT           PRIMARY KEY DEFAULT 1
FightDetection       BIT           NOT NULL DEFAULT 1
WeaponDetection      BIT           NOT NULL DEFAULT 1
IntrusionDetection   BIT           NOT NULL DEFAULT 1
FaceRecognition      BIT           NOT NULL DEFAULT 1
LicensePlate         BIT           NOT NULL DEFAULT 1
GlobalConfidence     DECIMAL(4,2)  NOT NULL DEFAULT 0.85
AlertLatencyTarget   INT           NOT NULL DEFAULT 2
FrameProcessingRate  INT           NOT NULL DEFAULT 30
GpuAcceleration      BIT           NOT NULL DEFAULT 1
ModelVersion         NVARCHAR(20)  NOT NULL DEFAULT 'YOLOv8n'
UpdatedAt            DATETIME2     NOT NULL DEFAULT GETUTCDATE()
UpdatedBy            UNIQUEIDENTIFIER NULL REFERENCES Users(Id)
```

---

## 4. API Endpoints

Base URL: `https://api.cctvguard.com/api`  
All protected endpoints require: `Authorization: Bearer <accessToken>`

---

### 4.1 Authentication  `/api/auth`

#### `POST /api/auth/login`
**Auth:** None  
**Body:**
```json
{ "username": "admin", "password": "password123" }
```
**Response 200:**
```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "d4f8a2...",
  "expiresIn": 3600,
  "user": {
    "id": "usr-001",
    "username": "admin",
    "email": "admin@cctvguard.com",
    "role": "Admin"
  }
}
```
**Response 401:** Invalid credentials  
**Side effect:** Creates a `UserSessions` record with `LoginAt = NOW()`

---

#### `POST /api/auth/refresh`
**Auth:** None  
**Body:** `{ "refreshToken": "d4f8a2..." }`  
**Response 200:** Same shape as login response  
**Response 401:** Token expired or revoked

---

#### `POST /api/auth/logout`
**Auth:** Bearer token  
**Body:** `{ "refreshToken": "d4f8a2..." }`  
**Response 204:** No content  
**Side effect:** Revokes refresh token; updates `UserSessions.LogoutAt` and computes `DurationMin`

---

#### `GET /api/auth/me`
**Auth:** Bearer token  
**Response 200:**
```json
{
  "id": "usr-001",
  "username": "admin",
  "email": "admin@cctvguard.com",
  "role": "Admin",
  "status": "active",
  "lastLogin": "2025-05-01T10:00:00Z"
}
```

---

### 4.2 Dashboard Stats  `/api/dashboard`

#### `GET /api/dashboard/stats`
**Auth:** Bearer  All roles  
**Response 200:**
```json
{
  "totalCameras": 6,
  "onlineCameras": 4,
  "todayIncidents": 3,
  "activeAlerts": 5,
  "systemUptime": "99.7%",
  "avgLatency": 1.8,
  "detectionAccuracy": 96.2
}
```

---

### 4.3 Cameras  `/api/cameras`

#### `GET /api/cameras`
**Auth:** Bearer  All roles  
**Response 200:** Array of camera objects
```json
[
  {
    "id": "cam-01",
    "name": "Main Entrance",
    "location": "Building A - Gate 1",
    "ipAddress": "192.168.1.101",
    "port": 554,
    "status": "online",
    "streamUrl": null,
    "detectionEnabled": true,
    "confidenceThreshold": 0.85,
    "frameRate": 30,
    "lastSeen": "2025-05-01T10:00:00Z"
  }
]
```

---

#### `POST /api/cameras`
**Auth:** Bearer  Admin only  
**Body:**
```json
{
  "name": "New Camera",
  "location": "Building D",
  "ipAddress": "192.168.1.110",
  "port": 554,
  "detectionEnabled": true,
  "confidenceThreshold": 0.85,
  "frameRate": 30
}
```
**Response 201:** Created camera object with generated `id`

---

#### `PUT /api/cameras/{id}`
**Auth:** Bearer  Admin only  
**Body:** Same as POST (all fields optional except id)  
**Response 200:** Updated camera object

---

#### `PATCH /api/cameras/{id}/detection`
**Auth:** Bearer  Admin or Operator  
**Body:** `{ "detectionEnabled": true }`  
**Response 200:** `{ "id": "cam-01", "detectionEnabled": true }`  
**Note:** This is the only camera endpoint Operators can call.

---

#### `DELETE /api/cameras/{id}`
**Auth:** Bearer  Admin only  
**Response 204:** No content

---

### 4.4 Incidents  `/api/incidents`

#### `GET /api/incidents`
**Auth:** Bearer  All roles  
**Query params:**
- `type`  fight | weapon | intrusion | unknown_face | license_plate
- `severity`  critical | high | medium | low
- `status`  new | acknowledged | resolved
- `cameraId`  filter by camera
- `search`  text search on type, cameraName
- `page`  default 1
- `pageSize`  default 50

**Response 200:**
```json
{
  "data": [ { ...incident } ],
  "total": 120,
  "page": 1,
  "pageSize": 50
}
```

---

#### `GET /api/incidents/{id}`
**Auth:** Bearer  All roles  
**Response 200:** Single incident object with full details

---

#### `PATCH /api/incidents/{id}/acknowledge`
**Auth:** Bearer  Admin or Operator  
**Response 200:** Updated incident with `status: "acknowledged"`, `acknowledgedBy`, `acknowledgedAt`

---

#### `PATCH /api/incidents/{id}/resolve`
**Auth:** Bearer  Admin or Operator  
**Body (optional):** `{ "notes": "Situation handled." }`  
**Response 200:** Updated incident with `status: "resolved"`, `resolvedBy`, `resolvedAt`

---

### 4.5 Alerts  `/api/alerts`

#### `GET /api/alerts`
**Auth:** Bearer  All roles  
**Query params:**
- `severity`  critical | high | medium | low
- `dismissed`  true | false (default false)

**Response 200:** Array of alert objects with per-user read/dismiss state
```json
[
  {
    "id": "alr-001",
    "incidentId": "inc-001",
    "type": "Fight Detected",
    "message": "Physical altercation detected at Main Entrance.",
    "cameraName": "Main Entrance",
    "severity": "critical",
    "timestamp": "2025-05-01T09:55:00Z",
    "isRead": false,
    "isDismissed": false
  }
]
```
**Note:** `isRead` and `isDismissed` are per-user from `AlertReadStatus` table.

---

#### `PATCH /api/alerts/{id}/read`
**Auth:** Bearer  All roles  
**Response 204:** Marks alert as read for the calling user

---

#### `PATCH /api/alerts/read-all`
**Auth:** Bearer  All roles  
**Response 204:** Marks all active alerts as read for the calling user

---

#### `PATCH /api/alerts/{id}/dismiss`
**Auth:** Bearer  Admin or Operator  
**Response 204:** Marks alert as dismissed for the calling user

---

### 4.6 Users  `/api/users`

#### `GET /api/users`
**Auth:** Bearer  Admin only  
**Query params:** `role`, `status`, `search`  
**Response 200:**
```json
[
  {
    "id": "usr-001",
    "username": "admin",
    "email": "admin@cctvguard.com",
    "role": "Admin",
    "status": "active",
    "createdAt": "2024-01-10T00:00:00Z",
    "lastLogin": "2025-05-01T09:00:00Z"
  }
]
```

---

#### `POST /api/users`
**Auth:** Bearer  Admin only  
**Body:**
```json
{
  "username": "op.john",
  "email": "john@cctvguard.com",
  "password": "SecurePass123",
  "role": "Operator",
  "status": "active"
}
```
**Response 201:** Created user (no password in response)  
**Validation:** Unique username, unique email, password min 8 chars

---

#### `PUT /api/users/{id}`
**Auth:** Bearer  Admin only  
**Body:** Same as POST; `password` is optional (omit to keep existing)  
**Response 200:** Updated user object

---

#### `PATCH /api/users/{id}/status`
**Auth:** Bearer  Admin only  
**Body:** `{ "status": "suspended" }`  
**Response 200:** Updated user  
**Validation:** Cannot suspend the last active Admin

---

#### `PATCH /api/users/{id}/role`
**Auth:** Bearer  Admin only  
**Body:** `{ "role": "Viewer" }`  
**Response 200:** Updated user  
**Validation:** Cannot demote the last Admin

---

#### `DELETE /api/users/{id}`
**Auth:** Bearer  Admin only  
**Response 204:** No content  
**Validation:** Cannot delete the last Admin

---

### 4.7 AI Settings  `/api/settings/ai`

#### `GET /api/settings/ai`
**Auth:** Bearer  Admin only  
**Response 200:**
```json
{
  "fightDetection": true,
  "weaponDetection": true,
  "intrusionDetection": true,
  "faceRecognition": true,
  "licensePlate": true,
  "globalConfidence": 0.85,
  "alertLatencyTarget": 2,
  "frameProcessingRate": 30,
  "gpuAcceleration": true,
  "modelVersion": "YOLOv8n"
}
```

---

#### `PUT /api/settings/ai`
**Auth:** Bearer  Admin only  
**Body:** Same shape as GET response  
**Response 200:** Updated settings

---

### 4.8 Analytics  `/api/analytics`

All analytics endpoints are **Admin only**.

---

#### `GET /api/analytics/user-sessions`
**Query params:** `year` (required), `month` (optional, 1-12), `role` (optional)  
**Response 200:**
```json
[
  {
    "userId": "usr-002",
    "username": "op.ahmed",
    "role": "Operator",
    "year": 2025,
    "month": 1,
    "totalSessions": 4,
    "totalHoursActive": 30.5
  }
]
```
**Source:** Aggregated from `UserSessions` table grouped by user + year + month.

---

#### `GET /api/analytics/camera-detections`
**Response 200:**
```json
[
  {
    "cameraId": "cam-01",
    "cameraName": "Main Entrance",
    "totalDetections": 142,
    "fightCount": 18,
    "weaponCount": 7,
    "intrusionCount": 32,
    "unknownFaceCount": 61,
    "licensePlateCount": 24
  }
]
```
**Source:** Aggregated from `Incidents` table grouped by `CameraId` and `Type`.

---

#### `GET /api/analytics/monthly-alerts`
**Query params:** `year` (required)  
**Response 200:**
```json
[
  {
    "month": 1,
    "year": 2025,
    "label": "Jan 2025",
    "total": 12,
    "critical": 3,
    "high": 5,
    "medium": 3,
    "low": 1
  }
]
```
**Source:** Aggregated from `Alerts` table grouped by month/year and `Severity`.

---

#### `GET /api/analytics/camera-alerts`
**Response 200:**
```json
[
  {
    "cameraId": "cam-01",
    "cameraName": "Main Entrance",
    "total": 58,
    "critical": 14,
    "high": 22
  }
]
```
**Source:** Aggregated from `Alerts` joined with `Incidents` grouped by camera.

---

#### `GET /api/analytics/overview`
**Query params:** `year` (required)  
**Response 200:**
```json
{
  "totalActiveHours": 1240,
  "operatorHours": 860,
  "viewerHours": 380,
  "totalDetections": 538,
  "totalAlerts": 251
}
```

---

### 4.9 System Info  `/api/system`

#### `GET /api/system/info`
**Auth:** Bearer  Admin only  
**Response 200:**
```json
{
  "uptime": "99.7%",
  "avgLatency": 1.8,
  "detectionAccuracy": 96.2,
  "frameLatencyMs": 12,
  "cloudProvider": "AWS / GCP",
  "database": "SQL Server",
  "backendVersion": "1.0.0"
}
```

---

## 5. SignalR Hub  Real-Time Alerts

**Hub URL:** `wss://api.cctvguard.com/hubs/alerts`  
**Auth:** JWT token passed as query param `?access_token=<token>` or via `Authorization` header

### Events pushed from server to client:

| Event | Payload | Description |
|---|---|---|
| `NewAlert` | Alert object | Fired when AI detects a new threat |
| `IncidentUpdated` | `{ id, status }` | Fired when an incident is acknowledged or resolved |
| `CameraStatusChanged` | `{ id, status }` | Fired when a camera goes online/offline/error |
| `AlertDismissed` | `{ alertId, userId }` | Fired when an alert is dismissed |

The Angular frontend will subscribe to these events to update the live monitor, alert bell badge, and incident list in real time without polling.

---

## 6. HTTP Interceptor Requirements (Angular Frontend)

When the backend is integrated, the Angular `AuthService` and an HTTP interceptor must be updated to:

1. **Attach JWT**  add `Authorization: Bearer <token>` to every outgoing request
2. **Handle 401**  on receiving a 401, automatically call `POST /api/auth/refresh`, retry the original request with the new token
3. **Handle 403**  show an "Access Denied" toast and redirect to `/dashboard/monitor`
4. **Handle token expiry**  if refresh also fails (401), call `logout()` and redirect to `/login`
5. **Session logging**  call `POST /api/auth/logout` on `logout()` to close the session record

---

## 7. Session Logging Requirements

The `UserSessions` table powers the **Analytics > User Activity** tab. The backend must:

1. **On login** (`POST /api/auth/login`)  insert a new `UserSessions` row with `LoginAt = UTC_NOW`
2. **On logout** (`POST /api/auth/logout`)  update the row: set `LogoutAt = UTC_NOW`, compute `DurationMin = DATEDIFF(minute, LoginAt, LogoutAt)`
3. **On token expiry**  if a refresh token expires without explicit logout, a background job should close any open sessions older than the refresh token TTL (7 days)
4. **Analytics aggregation**  the `GET /api/analytics/user-sessions` endpoint aggregates `UserSessions` by user, year, and month, summing `DurationMin / 60` to get `totalHoursActive`

---

## 8. Validation Rules (enforced server-side)

| Field | Rule |
|---|---|
| `username` | Required, 3-50 chars, alphanumeric + dots/underscores, unique |
| `email` | Required, valid email format, unique |
| `password` | Min 8 chars, must contain letters and numbers |
| `role` | Must be one of: Admin, Operator, Viewer |
| `status` | Must be one of: active, suspended |
| `ipAddress` | Valid IPv4 or IPv6 format |
| `port` | Integer 165535 |
| `confidenceThreshold` | Decimal 0.01.0 |
| `frameRate` | Positive integer 1120 |
| Last Admin protection | Cannot delete, suspend, or demote the last Admin user |

---

## 9. Security Requirements

- All endpoints use **HTTPS only** (TLS 1.2+)
- Passwords stored as **BCrypt** hashes (cost factor  12)
- JWT signed with **RS256** (asymmetric) or **HS256** with a 256-bit secret
- JWT access token TTL: **60 minutes**
- Refresh token TTL: **7 days**, stored hashed in DB
- **Rate limiting** on `/api/auth/login`  max 10 attempts per IP per minute
- **CORS**  allow only the Angular app origin in production
- All inputs sanitized to prevent SQL injection (EF Core parameterized queries handle this)
- Role claims validated server-side on every request  never trust client-side role checks alone

---

## 10. ASP.NET Core Project Structure

```
CctvGuard.Api/
 Controllers/
    AuthController.cs
    CamerasController.cs
    IncidentsController.cs
    AlertsController.cs
    UsersController.cs
    SettingsController.cs
    AnalyticsController.cs
    SystemController.cs
 Hubs/
    AlertsHub.cs
 Models/
    Entities/
       User.cs
       RefreshToken.cs
       Camera.cs
       Incident.cs
       Alert.cs
       AlertReadStatus.cs
       UserSession.cs
       AiSettings.cs
    DTOs/
        Auth/
           LoginRequest.cs
           LoginResponse.cs
           RefreshRequest.cs
        Camera/
           CameraDto.cs
           CreateCameraRequest.cs
           UpdateCameraRequest.cs
        Incident/
           IncidentDto.cs
           IncidentFilterParams.cs
        Alert/
           AlertDto.cs
        User/
           UserDto.cs
           CreateUserRequest.cs
           UpdateUserRequest.cs
        Analytics/
            UserSessionSummaryDto.cs
            CameraDetectionStatDto.cs
            MonthlyAlertStatDto.cs
            AnalyticsOverviewDto.cs
 Services/
    AuthService.cs
    TokenService.cs
    UserService.cs
    CameraService.cs
    IncidentService.cs
    AlertService.cs
    AnalyticsService.cs
    SessionService.cs
 Data/
    AppDbContext.cs
    Migrations/
 Middleware/
    ExceptionHandlingMiddleware.cs
 appsettings.json
 Program.cs
```

---

## 11. `appsettings.json` Configuration Keys

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=CctvGuardDb;Trusted_Connection=True;"
  },
  "Jwt": {
    "Secret": "<256-bit-secret-key>",
    "Issuer": "CctvGuardApi",
    "Audience": "CctvGuardClient",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 7
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:4200", "https://cctvguard.com"]
  }
}
```

---

## 12. NuGet Packages Required

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.*" />
<PackageReference Include="BCrypt.Net-Next" Version="4.*" />
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="8.*" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.*" />
<PackageReference Include="AutoMapper.Extensions.Microsoft.DependencyInjection" Version="12.*" />
```

---

## 13. Angular Frontend Integration Checklist

Once the backend is ready, the following changes are needed in the Angular project:

- [ ] Replace `MockDataService` with real HTTP services using `HttpClient`
- [ ] Create `AuthInterceptor` to attach JWT and handle 401/403 responses
- [ ] Update `AuthService.login()` to call `POST /api/auth/login`
- [ ] Update `AuthService.logout()` to call `POST /api/auth/logout`
- [ ] Add `AuthService.refreshToken()` called automatically by the interceptor
- [ ] Update `authGuard` to also validate token expiry client-side
- [ ] Connect `AlertsHub` SignalR client for real-time alert push
- [ ] Update `environment.ts` with `apiUrl` and `hubUrl`
- [ ] Handle loading states and error toasts for all HTTP calls

---

*Document version: 1.0  Generated for AI-Powered CCTV Guard FYP project*  
*University of Engineering and Technology Lahore  Department of Computer Science*
