import 'package:flutter/material.dart';
import 'services/notification_service.dart';
import 'services/auth_service.dart';
import 'services/alert_service.dart';
import 'screens/login_screen.dart';
import 'screens/dashboard_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Initialize local notifications before app starts
  await NotificationService().initialize();

  // Check if a saved token exists — auto-login
  final savedToken = await AuthService().getSavedToken();
  if (savedToken != null) {
    AlertService().setToken(savedToken);
  }

  runApp(CctvGuardApp(isLoggedIn: savedToken != null));
}

class CctvGuardApp extends StatelessWidget {
  final bool isLoggedIn;
  const CctvGuardApp({super.key, required this.isLoggedIn});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'CCTV Guard',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
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
          titleTextStyle: TextStyle(
            color: Colors.white,
            fontSize: 18,
            fontWeight: FontWeight.w700,
          ),
        ),
        cardTheme: CardTheme(
          color: const Color(0xFF111827),
          elevation: 0,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(12),
            side: const BorderSide(color: Color(0xFF1f2937)),
          ),
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: const Color(0xFF1f2937),
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: Color(0xFF374151)),
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: Color(0xFF374151)),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: Color(0xFF3b82f6), width: 2),
          ),
          labelStyle: const TextStyle(color: Color(0xFF9ca3af)),
          hintStyle: const TextStyle(color: Color(0xFF6b7280)),
        ),
        elevatedButtonTheme: ElevatedButtonThemeData(
          style: ElevatedButton.styleFrom(
            backgroundColor: const Color(0xFF3b82f6),
            foregroundColor: Colors.white,
            padding: const EdgeInsets.symmetric(vertical: 14),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
            textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
          ),
        ),
        useMaterial3: true,
      ),
      home: isLoggedIn ? const DashboardScreen() : const LoginScreen(),
      routes: {
        '/login':     (_) => const LoginScreen(),
        '/dashboard': (_) => const DashboardScreen(),
      },
    );
  }
}
