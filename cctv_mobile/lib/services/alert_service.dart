import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import 'package:signalr_netcore/signalr_client.dart';
import '../models/alert_model.dart';
import '../models/emergency_notification_model.dart';
import 'notification_service.dart';

enum HubConnectionState { disconnected, connecting, connected, reconnecting }

/// Singleton that manages:
///  • SignalR real-time alert stream (NewAlert)
///  • SignalR emergency escalation stream (ReceiveEmergencyNotification)
///  • REST fetch of historical alerts
///  • Server URL persistence via SharedPreferences
class AlertService {
  static const String _defaultBaseUrl = 'http://192.168.127.138:5176';
  static const String _prefKey        = 'server_base_url';

  static final AlertService _instance = AlertService._internal();
  factory AlertService() => _instance;
  AlertService._internal();

  // ── Runtime base URL ──────────────────────────────────────────────────────
  String _baseUrl = _defaultBaseUrl;
  String get baseUrl => _baseUrl;

  Future<void> loadSavedUrl() async {
    final prefs = await SharedPreferences.getInstance();
    _baseUrl = prefs.getString(_prefKey) ?? _defaultBaseUrl;
  }

  Future<void> saveUrl(String url) async {
    _baseUrl = url.trim();
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefKey, _baseUrl);
  }

  // ── SignalR ───────────────────────────────────────────────────────────────
  HubConnection? _connection;
  final NotificationService _notifications = NotificationService();

  final _stateCtrl     = StreamController<HubConnectionState>.broadcast();
  final _alertCtrl     = StreamController<AlertModel>.broadcast();
  final _emergencyCtrl = StreamController<EmergencyNotificationModel>.broadcast();
  final _incidentUpdatedCtrl = StreamController<Map<String, String>>.broadcast();

  Stream<HubConnectionState>          get connectionState$    => _stateCtrl.stream;
  Stream<AlertModel>                  get alerts$             => _alertCtrl.stream;
  /// Emits whenever an operator escalates an alert to emergency services.
  Stream<EmergencyNotificationModel>  get emergency$          => _emergencyCtrl.stream;
  /// Emits {id, status} when any incident status changes (acknowledge/resolve).
  Stream<Map<String, String>>         get incidentUpdated$    => _incidentUpdatedCtrl.stream;

  HubConnectionState _state = HubConnectionState.disconnected;
  HubConnectionState get currentState => _state;

  String? _jwtToken;
  void setToken(String token) => _jwtToken = token;

  Future<void> connect() async {
    if (_state == HubConnectionState.connected ||
        _state == HubConnectionState.connecting) return;

    _setState(HubConnectionState.connecting);

    try {
      final hubUrl = '$_baseUrl/hubs/alerts';

      final options = HttpConnectionOptions(
        accessTokenFactory: _jwtToken != null ? () async => _jwtToken! : null,
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
        logMessageContent: false,
      );

      _connection = HubConnectionBuilder()
          .withUrl(hubUrl, options: options)
          .withAutomaticReconnect(retryDelays: [0, 2000, 5000, 10000, 30000])
          .build();

      // ── Standard alert — goes to Alerts tab only ──────────────────────────
      _connection!.on('NewAlert', (List<Object?>? args) {
        if (args == null || args.isEmpty) return;
        try {
          final raw = args[0];
          if (raw is Map<String, dynamic>) {
            final alert = AlertModel.fromJson(raw);
            _alertCtrl.add(alert);
            // Standard alerts do NOT trigger a push notification on mobile —
            // they are listed in the Alerts tab only.
          }
        } catch (_) {}
      });

      // ── Emergency escalation — goes to Notifications tab + push notification ──
      _connection!.on('ReceiveEmergencyNotification', (List<Object?>? args) {
        if (args == null || args.isEmpty) return;
        try {
          final raw = args[0];
          if (raw is Map<String, dynamic>) {
            final emergency = EmergencyNotificationModel.fromJson(raw);
            _emergencyCtrl.add(emergency);
            _notifications.showEmergencyNotification(emergency);
          }
        } catch (_) {}
      });

      // ── Incident status update (acknowledge / resolve from any client) ──
      _connection!.on('IncidentUpdated', (List<Object?>? args) {
        if (args == null || args.isEmpty) return;
        try {
          final raw = args[0];
          if (raw is Map<String, dynamic>) {
            final id     = raw['id']     as String? ?? '';
            final status = raw['status'] as String? ?? '';
            if (id.isNotEmpty) _incidentUpdatedCtrl.add({'id': id, 'status': status});
          }
        } catch (_) {}
      });

      _connection!.onreconnecting(({Exception? error}) =>
          _setState(HubConnectionState.reconnecting));
      _connection!.onreconnected(({String? connectionId}) =>
          _setState(HubConnectionState.connected));
      _connection!.onclose(({Exception? error}) =>
          _setState(HubConnectionState.disconnected));

      await _connection!.start();
      _setState(HubConnectionState.connected);
    } catch (e) {
      _setState(HubConnectionState.disconnected);
      rethrow;
    }
  }

  Future<void> disconnect() async {
    try { await _connection?.stop(); } catch (_) {}
    _connection = null;
    _setState(HubConnectionState.disconnected);
  }

  // ── REST: fetch historical alerts ─────────────────────────────────────────
  Future<List<AlertModel>> fetchAlerts({String? severity}) async {
    if (_jwtToken == null) return [];
    try {
      final uri = Uri.parse('$_baseUrl/api/alerts').replace(
        queryParameters: severity != null ? {'severity': severity} : null,
      );
      final res = await http.get(uri, headers: {
        'Authorization': 'Bearer $_jwtToken',
        'Content-Type': 'application/json',
      }).timeout(const Duration(seconds: 10));

      if (res.statusCode == 200) {
        final list = jsonDecode(res.body) as List<dynamic>;
        return list
            .map((e) => AlertModel.fromJson(e as Map<String, dynamic>))
            .toList();
      }
    } catch (_) {}
    return [];
  }

  // ── REST: mark alert read ─────────────────────────────────────────────────
  Future<void> markRead(String alertId) async {
    if (_jwtToken == null) return;
    try {
      await http.patch(
        Uri.parse('$_baseUrl/api/alerts/$alertId/read'),
        headers: {'Authorization': 'Bearer $_jwtToken'},
      ).timeout(const Duration(seconds: 5));
    } catch (_) {}
  }

  // ── REST: acknowledge incident ────────────────────────────────────────────
  /// Acknowledges an incident and triggers IncidentUpdated on all SignalR clients.
  Future<bool> acknowledgeIncident(String incidentId) async {
    if (_jwtToken == null) return false;
    try {
      final res = await http.patch(
        Uri.parse('$_baseUrl/api/incidents/$incidentId/acknowledge'),
        headers: {
          'Authorization': 'Bearer $_jwtToken',
          'Content-Type': 'application/json',
        },
      ).timeout(const Duration(seconds: 10));
      return res.statusCode == 200;
    } catch (_) {
      return false;
    }
  }

  void _setState(HubConnectionState s) {
    _state = s;
    if (!_stateCtrl.isClosed) _stateCtrl.add(s);
  }
}
