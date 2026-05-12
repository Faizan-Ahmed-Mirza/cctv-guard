import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:firebase_core/firebase_core.dart';
import 'services/notification_service.dart';
import 'services/auth_service.dart';
import 'services/alert_service.dart';
import 'screens/login_screen.dart';
import 'screens/main_screen.dart';

void main() {
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();

    await SystemChrome.setPreferredOrientations([
      DeviceOrientation.portraitUp,
      DeviceOrientation.portraitDown,
    ]);

    // 1. SILENT Firebase Init (With Timeout)
    // This prevents the red screen if the API key is invalid
    try {
      await Firebase.initializeApp().timeout(const Duration(seconds: 2));
    } catch (e) {
      debugPrint('Skipping Firebase: $e');
    }

    // 2. SILENT Notification Init
    try {
      await NotificationService().initialize().timeout(const Duration(seconds: 2));
    } catch (_) {}

    // 3. App Config
    await AlertService().loadSavedUrl();

    bool isLoggedIn = false;
    try {
      final token = await AuthService().getSavedToken();
      if (token != null && token.isNotEmpty) {
        AlertService().setToken(token);
        isLoggedIn = true;
      }
    } catch (_) {}

    runApp(CctvGuardApp(isLoggedIn: isLoggedIn));
  }, (error, stack) {
    // Fallback UI only for absolute critical failures
    debugPrint('Zoned Error: $error');
    runApp(MaterialApp(
      home: Scaffold(
        backgroundColor: Colors.black,
        body: Center(child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text('App started with an issue. Please restart.\nDetail: $error',
              style: const TextStyle(color: Colors.red, fontSize: 12),
              textAlign: TextAlign.center),
        )),
      ),
    ));
  });
}

class CctvGuardApp extends StatelessWidget {
  final bool isLoggedIn;
  const CctvGuardApp({super.key, required this.isLoggedIn});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'CCTV Guard',
      debugShowCheckedModeBanner: false,
      theme: _buildTheme(),
      // Force initial route to respect login status
      initialRoute: isLoggedIn ? '/home' : '/login',
      routes: {
        '/login': (_) => const LoginScreen(),
        '/home':  (_) => const MainScreen(),
      },
    );
  }

  ThemeData _buildTheme() => ThemeData(
    brightness: Brightness.dark,
    scaffoldBackgroundColor: const Color(0xFF0a0a0f),
    colorScheme: const ColorScheme.dark(
      primary:   Color(0xFF3b82f6),
      secondary: Color(0xFF6366f1),
      surface:   Color(0xFF111827),
      error:     Color(0xFFef4444),
    ),
    appBarTheme: const AppBarTheme(
      backgroundColor: Color(0xFF111827),
      elevation: 0,
      iconTheme: IconThemeData(color: Colors.white),
      titleTextStyle: TextStyle(color: Colors.white, fontSize: 17, fontWeight: FontWeight.w700),
    ),
    bottomNavigationBarTheme: const BottomNavigationBarThemeData(
      backgroundColor: Color(0xFF111827),
      selectedItemColor: Color(0xFF3b82f6),
      unselectedItemColor: Color(0xFF6b7280),
      type: BottomNavigationBarType.fixed,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: const Color(0xFF1f2937),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(8),
          borderSide: const BorderSide(color: Color(0xFF374151))),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8),
          borderSide: const BorderSide(color: Color(0xFF374151))),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8),
          borderSide: const BorderSide(color: Color(0xFF3b82f6), width: 2)),
      labelStyle: const TextStyle(color: Color(0xFF9ca3af)),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: const Color(0xFF3b82f6),
        foregroundColor: Colors.white,
        padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 24),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
    ),
    useMaterial3: true,
  );
}
