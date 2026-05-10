# CCTV Guard — Flutter Companion App

Real-time threat alert companion for the CCTV Guard surveillance system.
Connects to the .NET backend via SignalR and shows native push notifications
when weapons, fights, fire, or intrusions are detected.

---

## Setup

### 1. Install Flutter
Download from https://flutter.dev/docs/get-started/install
Add Flutter to PATH, then run: `flutter doctor`

### 2. Set your server IP
Open `lib/services/alert_service.dart` and change:
```dart
static const String backendBaseUrl = 'https://192.168.1.100:7225';
```
Replace `192.168.1.100` with your laptop's Wi-Fi IP address.
Find it with: `ipconfig` → look for "IPv4 Address" under Wi-Fi adapter.

### 3. Install dependencies
```bash
cd cctv_mobile
flutter pub get
```

### 4. Run on Android device / emulator
```bash
flutter run
```

Or build APK:
```bash
flutter build apk --release
```
APK will be at: `build/app/outputs/flutter-apk/app-release.apk`

---

## Architecture

```
lib/
├── main.dart                    # App entry point, theme, routing
├── models/
│   └── alert_model.dart         # Alert data model
├── services/
│   ├── alert_service.dart       # SignalR connection + event handling
│   ├── auth_service.dart        # Login / JWT token management
│   └── notification_service.dart # Local push notifications
└── screens/
    ├── login_screen.dart        # Login UI
    └── dashboard_screen.dart    # Main dashboard with alert list
```

## Features

- **Real-time alerts** via SignalR WebSocket connection
- **Native push notifications** — appear even when app is in background
- **Dark theme** matching the Angular web UI
- **Connection status** indicator (Online/Offline/Reconnecting)
- **Alert history** for the current session (last 100 alerts)
- **Severity color coding** — Critical (red), High (orange), Medium (yellow), Low (green)
- **Auto-reconnect** with exponential backoff (0s, 2s, 5s, 10s, 30s)
- **JWT authentication** — token saved to SharedPreferences

## Default Credentials
- Username: `admin`
- Password: `Admin@1234`

## Dependencies
| Package | Purpose |
|---------|---------|
| `signalr_netcore` | SignalR WebSocket client |
| `flutter_local_notifications` | Native push notifications |
| `http` | REST API calls (login) |
| `shared_preferences` | JWT token persistence |
| `intl` | Date/time formatting |
