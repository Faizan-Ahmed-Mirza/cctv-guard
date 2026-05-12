import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import 'alert_service.dart';
import 'notification_service.dart';

class AuthService {
  static const String _tokenKey = 'cctv_jwt_token';
  static const String _userKey  = 'cctv_username';

  static final AuthService _instance = AuthService._internal();
  factory AuthService() => _instance;
  AuthService._internal();

  String? _cachedToken;
  String? _cachedUsername;

  String? get cachedUsername => _cachedUsername;

  Future<bool> login(String username, String password) async {
    try {
      await AlertService().loadSavedUrl();
      final uri = Uri.parse('${AlertService().baseUrl}/api/auth/login');

      final res = await http.post(
        uri,
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'username': username, 'password': password}),
      ).timeout(const Duration(seconds: 15));

      if (res.statusCode == 200) {
        final data  = jsonDecode(res.body) as Map<String, dynamic>;
        final token = data['accessToken'] as String?;
        if (token != null && token.isNotEmpty) {
          await _persist(token, username);
          AlertService().setToken(token);

          // Register FCM token with backend (best-effort)
          _registerFcmToken(token);
          return true;
        }
      }
    } catch (_) {}
    return false;
  }

  Future<String?> getSavedToken() async {
    final prefs = await SharedPreferences.getInstance();
    _cachedToken    = prefs.getString(_tokenKey);
    _cachedUsername = prefs.getString(_userKey);
    return _cachedToken;
  }

  Future<void> logout() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_tokenKey);
    await prefs.remove(_userKey);
    _cachedToken    = null;
    _cachedUsername = null;
    await AlertService().disconnect();
    await NotificationService().cancelAll();
  }

  Future<void> _persist(String token, String username) async {
    _cachedToken    = token;
    _cachedUsername = username;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_tokenKey, token);
    await prefs.setString(_userKey, username);
  }

  /// Best-effort: send FCM device token to backend so it can push to this device
  Future<void> _registerFcmToken(String jwtToken) async {
    try {
      final fcmToken = await NotificationService().getFcmToken();
      if (fcmToken == null) return;
      await http.post(
        Uri.parse('${AlertService().baseUrl}/api/auth/fcm-token'),
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer $jwtToken',
        },
        body: jsonEncode({'fcmToken': fcmToken}),
      ).timeout(const Duration(seconds: 5));
    } catch (_) {
      // Non-critical — app still works via SignalR
    }
  }
}
