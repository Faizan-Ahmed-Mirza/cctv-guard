import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import 'alert_service.dart';

class AuthService {
  static const String _tokenKey = 'cctv_jwt_token';

  /// Login with username/password. Returns true on success.
  Future<bool> login(String username, String password) async {
    try {
      final uri = Uri.parse('${AlertService.backendBaseUrl}/api/auth/login');
      final response = await http.post(
        uri,
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'username': username, 'password': password}),
      ).timeout(const Duration(seconds: 15));

      if (response.statusCode == 200) {
        final data = jsonDecode(response.body) as Map<String, dynamic>;
        final token = data['accessToken'] as String?;
        if (token != null) {
          await _saveToken(token);
          AlertService().setToken(token);
          return true;
        }
      }
      return false;
    } catch (_) {
      return false;
    }
  }

  Future<String?> getSavedToken() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(_tokenKey);
  }

  Future<void> _saveToken(String token) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_tokenKey, token);
  }

  Future<void> logout() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_tokenKey);
    await AlertService().disconnect();
  }
}
