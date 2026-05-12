import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:firebase_core/firebase_core.dart';
import '../models/alert_model.dart';

/// Handles both:
///  1. Firebase Cloud Messaging (FCM) — push when app is background/killed
///  2. flutter_local_notifications — heads-up banner when app is foreground
@pragma('vm:entry-point')
Future<void> firebaseBackgroundHandler(RemoteMessage message) async {
  // Background FCM messages are shown automatically by the OS on Android 8+.
}

class NotificationService {
  static final NotificationService _instance = NotificationService._internal();
  factory NotificationService() => _instance;
  NotificationService._internal();

  final FlutterLocalNotificationsPlugin _local = FlutterLocalNotificationsPlugin();

  // Late initialization to avoid crashing if Firebase isn't ready
  FirebaseMessaging? _fcm;

  final _tapController = StreamController<String>.broadcast();
  Stream<String> get onNotificationTap$ => _tapController.stream;

  int _notifId = 0;
  bool _initialized = false;
  bool _fcmEnabled = false;

  static const _channel = AndroidNotificationChannel(
    'cctv_guard_alerts',
    'CCTV Guard Alerts',
    description: 'Real-time security threat alerts',
    importance: Importance.max,
    playSound: true,
    enableVibration: true,
  );

  Future<void> initialize() async {
    if (_initialized) return;

    try {
      // 1. Local Notifications (Always works, no Firebase needed)
      const androidInit = AndroidInitializationSettings('@android:drawable/ic_dialog_alert');
      const iosInit = DarwinInitializationSettings(
        requestAlertPermission: true, requestBadgePermission: true, requestSoundPermission: true,
      );
      await _local.initialize(
        const InitializationSettings(android: androidInit, iOS: iosInit),
        onDidReceiveNotificationResponse: (details) {
          if (details.payload != null) _tapController.add(details.payload!);
        },
      ).timeout(const Duration(seconds: 3));

      // 2. Optional FCM (Silently fail if API key is wrong)
      if (Firebase.apps.isNotEmpty) {
        try {
          _fcm = FirebaseMessaging.instance;

          await _fcm!.requestPermission(alert: true, badge: true, sound: true);
          FirebaseMessaging.onBackgroundMessage(firebaseBackgroundHandler);

          FirebaseMessaging.onMessage.listen((RemoteMessage message) {
            final notification = message.notification;
            if (notification == null) return;
            _local.show(_notifId++, notification.title ?? 'CCTV Alert', notification.body ?? '', null);
          });

          _fcmEnabled = true;
        } catch (_) {
          debugPrint('Firebase Messaging unavailable - API key likely invalid');
        }
      }
    } catch (e) {
      debugPrint('Notif Init Error: $e');
    }
    _initialized = true;
  }

  Future<void> showAlertNotification(AlertModel alert) async {
    if (!_initialized) return;

    final title = '${alert.icon} ${alert.type}';
    final body  = '${alert.cameraName} — ${alert.message}';

    await _local.show(
      _notifId++,
      title,
      body,
      NotificationDetails(
        android: AndroidNotificationDetails(
          _channel.id, _channel.name,
          channelDescription: _channel.description,
          importance: Importance.max,
          priority: Priority.high,
          styleInformation: BigTextStyleInformation(body),
          color: _severityColor(alert.severity),
          playSound: true,
          enableVibration: true,
          visibility: NotificationVisibility.public,
        ),
        iOS: const DarwinNotificationDetails(
          presentAlert: true, presentBadge: true, presentSound: true,
        ),
      ),
      payload: alert.id,
    );
  }

  Future<String?> getFcmToken() async {
    if (!_fcmEnabled || _fcm == null) return null;
    try {
      return await _fcm!.getToken();
    } catch (_) {
      return null;
    }
  }

  Future<void> cancelAll() => _local.cancelAll();

  Color _severityColor(String severity) {
    switch (severity.toLowerCase()) {
      case 'critical': return const Color(0xFFef4444);
      case 'high':     return const Color(0xFFf97316);
      case 'medium':   return const Color(0xFFeab308);
      default:         return const Color(0xFF22c55e);
    }
  }
}
