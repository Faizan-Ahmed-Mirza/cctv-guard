import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:firebase_core/firebase_core.dart';
import 'services/notification_service.dart';
import 'services/auth_service.dart';
import 'services/alert_service.dart';
import 'screens/splash_screen.dart';
import 'screens/login_screen.dart';
import 'screens/main_screen.dart';

void main() {
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();

    await SystemChrome.setPreferredOrientations([
      DeviceOrientation.portraitUp,
      DeviceOrientation.portraitDown,
    ]);

    // Show the app immediately with the splash screen —
    // all heavy init (Firebase, token check) happens inside SplashRouter
    runApp(const CctvGuardApp());
  }, (error, stack) {
    debugPrint('Fatal error: $error');
    runApp(MaterialApp(
      home: Scaffold(
        backgroundColor: Colors.black,
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              'Startup error. Please restart the app.\n\n$error',
              style: const TextStyle(color: Colors.red, fontSize: 12),
              textAlign: TextAlign.center,
            ),
          ),
        ),
      ),
    ));
  });
}

class CctvGuardApp extends StatelessWidget {
  const CctvGuardApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'CCTV Guard',
      debugShowCheckedModeBanner: false,
      theme: _buildTheme(),
      // Start with SplashRouter — it handles init and navigates to login/home
      home: const SplashRouter(),
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
      titleTextStyle: TextStyle(
          color: Colors.white, fontSize: 17, fontWeight: FontWeight.w700),
    ),
    bottomNavigationBarTheme: const BottomNavigationBarThemeData(
      backgroundColor: Color(0xFF111827),
      selectedItemColor: Color(0xFF3b82f6),
      unselectedItemColor: Color(0xFF6b7280),
      type: BottomNavigationBarType.fixed,
      elevation: 0,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: const Color(0xFF1f2937),
      contentPadding:
          const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: const BorderSide(color: Color(0xFF374151))),
      enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: const BorderSide(color: Color(0xFF374151))),
      focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide:
              const BorderSide(color: Color(0xFF3b82f6), width: 2)),
      labelStyle: const TextStyle(color: Color(0xFF9ca3af)),
      hintStyle: const TextStyle(color: Color(0xFF6b7280)),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: const Color(0xFF3b82f6),
        foregroundColor: Colors.white,
        disabledBackgroundColor: const Color(0xFF1f2937),
        padding:
            const EdgeInsets.symmetric(vertical: 14, horizontal: 24),
        shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8)),
        textStyle: const TextStyle(
            fontSize: 15, fontWeight: FontWeight.w600),
        elevation: 0,
      ),
    ),
    cardTheme: CardThemeData(
      color: const Color(0xFF111827),
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: const BorderSide(color: Color(0xFF1f2937)),
      ),
    ),
    useMaterial3: true,
  );
}

/// Handles all startup initialization while showing the splash screen.
/// Navigates to /home (auto-login) or /login when ready.
class SplashRouter extends StatefulWidget {
  const SplashRouter({super.key});
  @override
  State<SplashRouter> createState() => _SplashRouterState();
}

class _SplashRouterState extends State<SplashRouter> {
  @override
  void initState() {
    super.initState();
    _initialize();
  }

  Future<void> _initialize() async {
    // Ensure splash is visible for at least 1.5s so the animation plays fully
    final minDelay = Future.delayed(const Duration(milliseconds: 1500));

    // Run all init tasks in parallel
    await Future.wait([
      minDelay,
      _initFirebase(),
      _initNotifications(),
      _initApp(),
    ]);

    if (!mounted) return;

    // Check saved token for auto-login
    bool isLoggedIn = false;
    try {
      final token = await AuthService().getSavedToken();
      if (token != null && token.isNotEmpty) {
        AlertService().setToken(token);
        isLoggedIn = true;
      }
    } catch (_) {}

    if (!mounted) return;
    Navigator.pushReplacementNamed(context, isLoggedIn ? '/home' : '/login');
  }

  Future<void> _initFirebase() async {
    try {
      await Firebase.initializeApp()
          .timeout(const Duration(seconds: 3));
    } catch (_) {
      // Firebase not configured — app works via SignalR without it
    }
  }

  Future<void> _initNotifications() async {
    try {
      await NotificationService()
          .initialize()
          .timeout(const Duration(seconds: 3));
    } catch (_) {}
  }

  Future<void> _initApp() async {
    try {
      await AlertService().loadSavedUrl();
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) => const SplashScreen();
}
