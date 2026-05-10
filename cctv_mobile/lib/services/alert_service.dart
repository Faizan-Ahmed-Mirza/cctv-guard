import 'dart:async';
import 'package:signalr_netcore/signalr_client.dart';
import '../models/alert_model.dart';
import 'notification_service.dart';

/// Connection states for the SignalR hub
enum HubConnectionState { disconnected, connecting, connected, reconnecting }

/// Manages the SignalR connection to the CCTV Guard backend alerts hub.
///
/// ⚠️  IMPORTANT: Change [backendBaseUrl] to your laptop's Wi-Fi IP address.
///     Find it with: ipconfig → look for "IPv4 Address" under your Wi-Fi adapter.
///     Example: http://192.168.1.105:7225
///
/// The hub URL is: [backendBaseUrl]/hubs/alerts
class AlertService {
  // ── CONFIGURATION ──────────────────────────────────────────────────────────
  // TODO: Change this to your laptop's Wi-Fi IP address before running on device
  static const String backendBaseUrl = 'https://192.168.1.100:7225';
  static const String hubPath        = '/hubs/alerts';
  // ──────────────────────────────────────────────────────────────────────────

  static final AlertService _instance = AlertService._internal();
  factory AlertService() => _instance;
  AlertService._internal();

  HubConnection? _connection;
  final NotificationService _notifications = NotificationService();

  // Stream controllers — UI subscribes to these
  final _connectionStateController = StreamController<HubConnectionState>.broadcast();
  final _alertController           = StreamController<AlertModel>.broadcast();

  Stream<HubConnectionState> get connectionState$ => _connectionStateController.stream;
  Stream<AlertModel>          get alerts$          => _alertController.stream;

  HubConnectionState _currentState = HubConnectionState.disconnected;
  HubConnectionState get currentState => _currentState;

  String? _jwtToken;

  /// Set the JWT token before connecting (obtained from login).
  void setToken(String token) => _jwtToken = token;

  /// Connect to the SignalR alerts hub.
  Future<void> connect() async {
    if (_currentState == HubConnectionState.connected ||
        _currentState == HubConnectionState.connecting) return;

    _setState(HubConnectionState.connecting);

    try {
      final hubUrl = '$backendBaseUrl$hubPath';

      _connection = HubConnectionBuilder()
          .withUrl(
            hubUrl,
            options: HttpConnectionOptions(
              accessTokenFactory: _jwtToken != null
                  ? () async => _jwtToken!
                  : null,
              // Skip SSL certificate validation for development (self-signed cert)
              skipNegotiation: true,
              transport: HttpTransportType.WebSockets,
            ),
          )
          .withAutomaticReconnect(
            retryDelays: [0, 2000, 5000, 10000, 30000],
          )
          .build();

      // ── Event listeners ──────────────────────────────────────────────────

      // New alert from AI detection pipeline
      _connection!.on('NewAlert', (args) {
        if (args == null || args.isEmpty) return;
        try {
          final data = args[0] as Map<String, dynamic>;
          final alert = AlertModel.fromJson(data);
          _alertController.add(alert);
          _notifications.showAlertNotification(alert);
        } catch (e) {
          // Ignore malformed messages
        }
      });

      // Camera status changed
      _connection!.on('CameraStatusChanged', (args) {
        // Could emit camera status events if needed
      });

      // Reconnecting
      _connection!.onreconnecting(({error}) {
        _setState(HubConnectionState.reconnecting);
      });

      // Reconnected
      _connection!.onreconnected(({connectionId}) {
        _setState(HubConnectionState.connected);
      });

      // Closed
      _connection!.onclose(({error}) {
        _setState(HubConnectionState.disconnected);
      });

      await _connection!.start();
      _setState(HubConnectionState.connected);
    } catch (e) {
      _setState(HubConnectionState.disconnected);
      rethrow;
    }
  }

  /// Disconnect from the hub.
  Future<void> disconnect() async {
    await _connection?.stop();
    _connection = null;
    _setState(HubConnectionState.disconnected);
  }

  void _setState(HubConnectionState state) {
    _currentState = state;
    _connectionStateController.add(state);
  }

  void dispose() {
    disconnect();
    _connectionStateController.close();
    _alertController.close();
  }
}
