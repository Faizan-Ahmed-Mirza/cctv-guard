import 'dart:typed_data';
import 'package:flutter/material.dart' show Color;
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import '../models/alert_model.dart';

/// Manages local push notifications for incoming CCTV Guard alerts.
/// Notifications appear as native Android/iOS banners even when app is in background.
class NotificationService {
  static final NotificationService _instance = NotificationService._internal();
  factory NotificationService() => _instance;
  NotificationService._internal();

  final FlutterLocalNotificationsPlugin _plugin = FlutterLocalNotificationsPlugin();
  int _notificationId = 0;

  /// Call once at app startup before runApp().
  Future<void> initialize() async {
    const AndroidInitializationSettings androidSettings =
        AndroidInitializationSettings('@mipmap/ic_launcher');

    const DarwinInitializationSettings iosSettings = DarwinInitializationSettings(
      requestAlertPermission: true,
      requestBadgePermission: true,
      requestSoundPermission: true,
    );

    const InitializationSettings settings = InitializationSettings(
      android: androidSettings,
      iOS: iosSettings,
    );

    await _plugin.initialize(
      settings,
      onDidReceiveNotificationResponse: (NotificationResponse response) {
        // Notification tapped — could navigate to alerts screen
      },
    );

    // Create Android notification channel (HIGH importance = heads-up banner)
    const AndroidNotificationChannel channel = AndroidNotificationChannel(
      'cctv_guard_alerts',
      'CCTV Guard Alerts',
      description: 'Real-time security threat alerts from CCTV Guard',
      importance: Importance.max,
      playSound: true,
      enableVibration: true,
    );

    await _plugin
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.createNotificationChannel(channel);

    // Request POST_NOTIFICATIONS permission on Android 13+
    await _plugin
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.requestNotificationsPermission();
  }

  /// Show a native push notification for the given alert.
  Future<void> showAlertNotification(AlertModel alert) async {
    final String title = '${alert.icon} ${alert.type}';
    final String body  = '${alert.cameraName} — ${alert.message}';

    final AndroidNotificationDetails androidDetails = AndroidNotificationDetails(
      'cctv_guard_alerts',
      'CCTV Guard Alerts',
      channelDescription: 'Real-time security threat alerts',
      importance: Importance.max,
      priority: Priority.high,
      ticker: title,
      styleInformation: BigTextStyleInformation(body),
      color: _severityColor(alert.severity),
      enableLights: true,
      ledColor: _severityColor(alert.severity),
      ledOnMs: 500,
      ledOffMs: 500,
      playSound: true,
      enableVibration: true,
      vibrationPattern: Int64List.fromList([0, 500, 200, 500]),
      visibility: NotificationVisibility.public,
    );

    const DarwinNotificationDetails iosDetails = DarwinNotificationDetails(
      presentAlert: true,
      presentBadge: true,
      presentSound: true,
    );

    await _plugin.show(
      _notificationId++,
      title,
      body,
      NotificationDetails(android: androidDetails, iOS: iosDetails),
      payload: alert.id,
    );
  }

  Future<void> cancelAll() => _plugin.cancelAll();

  Color _severityColor(String severity) {
    switch (severity.toLowerCase()) {
      case 'critical': return const Color(0xFFef4444);
      case 'high':     return const Color(0xFFf97316);
      case 'medium':   return const Color(0xFFeab308);
      default:         return const Color(0xFF22c55e);
    }
  }
}
