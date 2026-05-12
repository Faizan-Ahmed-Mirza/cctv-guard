# CCTV Guard Mobile

Flutter companion app for the CCTV Guard AI surveillance system.

## Features

- **Real-time alerts** via SignalR WebSocket — instant threat notifications while app is open
- **Firebase push notifications (FCM)** — alerts delivered even when app is closed/background
- **Alert history** — fetches persisted alerts from the REST API with severity filtering
- **Notification inbox** — grouped by date, shows all received alerts
- **Configurable server URL** — change backend IP from Settings without rebuilding

## Quick Start

### 1. Set server IP
Open `lib/services/alert_service.dart` and change `_defaultBaseUrl` to your backend IP:
```dart
static const String _defaultBaseUrl = 'http://YOUR_WIFI_IP:7225';
```
Or change it at runtime in the app's **Settings** tab.

### 2. Firebase setup (for push notifications)
1. Go to [Firebase Console](https://console.firebase.google.com)
2. Create a project → Add Android app with package `com.cctvguard.mobile`
3. Download `google-services.json` → place in `android/app/`
4. For iOS: download `GoogleService-Info.plist` → place in `ios/Runner/`

> **Without Firebase:** The app still works fully via SignalR real-time connection.
> Firebase only adds background push when the app is killed.

### 3. Run
```bash
flutter pub get
flutter run
```

## Default credentials
- Username: `admin`
- Password: `Admin@1234`

## Architecture

```
lib/
├── main.dart                    # App entry, Firebase init, theme
├── models/
│   └── alert_model.dart         # Alert data model
├── services/
│   ├── alert_service.dart       # SignalR hub + REST API + URL persistence
│   ├── auth_service.dart        # Login, JWT, FCM token registration
│   └── notification_service.dart # FCM + local notifications
└── screens/
    ├── login_screen.dart        # Login UI
    ├── main_screen.dart         # Bottom nav shell
    ├── alerts_tab.dart          # Live + historical alerts with filters
    ├── notifications_tab.dart   # Notification inbox grouped by date
    └── settings_tab.dart        # Server URL, FCM token, app info
```

## How push notifications work

1. On login, the app gets the FCM device token and sends it to `POST /api/auth/fcm-token`
2. The .NET backend stores the token and uses Firebase Admin SDK to push alerts
3. When a threat is detected, the backend sends both:
   - SignalR `NewAlert` event (real-time, app must be connected)
   - FCM push notification (works even when app is killed)
