import 'dart:async';
import 'package:flutter/material.dart';
import '../services/alert_service.dart';
import '../services/auth_service.dart';
import '../models/alert_model.dart';
import '../models/emergency_notification_model.dart';
import 'alerts_tab.dart';
import 'notifications_tab.dart';
import 'settings_tab.dart';

class MainScreen extends StatefulWidget {
  const MainScreen({super.key});
  @override
  State<MainScreen> createState() => _MainScreenState();
}

class _MainScreenState extends State<MainScreen> {
  int _tab = 0;
  final AlertService _svc = AlertService();

  // Alerts tab — all live + historical alerts
  final List<AlertModel> _liveAlerts = [];

  // Notifications tab — ONLY operator-escalated emergencies
  final List<EmergencyNotificationModel> _emergencies = [];

  // Badge counts
  int _alertUnread     = 0; // unread on Alerts tab
  int _emergencyUnread = 0; // unread on Notifications tab

  late StreamSubscription<HubConnectionState>         _stateSub;
  late StreamSubscription<AlertModel>                 _alertSub;
  late StreamSubscription<EmergencyNotificationModel> _emergencySub;
  HubConnectionState _connState = HubConnectionState.disconnected;

  @override
  void initState() {
    super.initState();
    _connState = _svc.currentState;

    _stateSub = _svc.connectionState$.listen((s) {
      if (mounted) setState(() => _connState = s);
    });

    // Standard alerts → Alerts tab only, no notification
    _alertSub = _svc.alerts$.listen((alert) {
      if (mounted) {
        setState(() {
          _liveAlerts.insert(0, alert);
          if (_liveAlerts.length > 300) _liveAlerts.removeLast();
          if (_tab != 0) _alertUnread++;
        });
      }
    });

    // Emergency escalations → Notifications tab + push notification
    _emergencySub = _svc.emergency$.listen((emergency) {
      if (mounted) {
        setState(() {
          _emergencies.insert(0, emergency);
          if (_tab != 1) _emergencyUnread++;
        });
        _showEmergencyPopup(emergency);
      }
    });

    _fetchHistory();
    if (_connState == HubConnectionState.disconnected) _connect();
  }

  Future<void> _fetchHistory() async {
    final history = await _svc.fetchAlerts();
    if (mounted && history.isNotEmpty) {
      setState(() {
        final existingIds = _liveAlerts.map((a) => a.id).toSet();
        for (var a in history) {
          if (!existingIds.contains(a.id)) _liveAlerts.add(a);
        }
        _liveAlerts.sort((a, b) => b.timestamp.compareTo(a.timestamp));
      });
    }
  }

  @override
  void dispose() {
    _stateSub.cancel();
    _alertSub.cancel();
    _emergencySub.cancel();
    super.dispose();
  }

  Future<void> _connect() async {
    try { await _svc.connect(); } catch (_) {}
  }

  /// Full-screen emergency popup — shown when an operator escalates an alert
  void _showEmergencyPopup(EmergencyNotificationModel emergency) {
    showDialog(
      context: context,
      barrierDismissible: true,
      builder: (_) => _EmergencyDialog(emergency: emergency),
    );
  }

  void _onTab(int i) => setState(() {
    _tab = i;
    if (i == 0) _alertUnread     = 0;
    if (i == 1) _emergencyUnread = 0;
  });

  Future<void> _logout() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        backgroundColor: const Color(0xFF111827),
        title: const Text('Logout', style: TextStyle(color: Colors.white)),
        content: const Text('Are you sure you want to logout?',
            style: TextStyle(color: Color(0xFF9ca3af))),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.pop(context, true),
              child: const Text('Logout',
                  style: TextStyle(color: Color(0xFFef4444)))),
        ],
      ),
    );
    if (ok == true && mounted) {
      await AuthService().logout();
      Navigator.pushReplacementNamed(context, '/login');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Row(children: [
          Container(
            width: 32, height: 32,
            decoration: BoxDecoration(
              gradient: const LinearGradient(
                colors: [Color(0xFF3b82f6), Color(0xFF6366f1)],
                begin: Alignment.topLeft, end: Alignment.bottomRight,
              ),
              borderRadius: BorderRadius.circular(9),
            ),
            child: const Icon(Icons.videocam_rounded, color: Colors.white, size: 18),
          ),
          const SizedBox(width: 10),
          const Text('CCTV Guard'),
        ]),
        actions: [
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 4),
            child: _ConnChip(state: _connState, onTap: _connect),
          ),
          IconButton(
            icon: const Icon(Icons.logout, size: 20, color: Color(0xFF9ca3af)),
            onPressed: _logout,
          ),
        ],
      ),
      body: IndexedStack(
        index: _tab,
        children: [
          AlertsTab(
            liveAlerts: _liveAlerts,
            connState: _connState,
            onConnect: _connect,
            onClear: () => setState(() { _liveAlerts.clear(); _alertUnread = 0; }),
          ),
          NotificationsTab(emergencies: _emergencies),
          const SettingsTab(),
        ],
      ),
      bottomNavigationBar: Container(
        decoration: const BoxDecoration(
          border: Border(top: BorderSide(color: Color(0xFF1f2937))),
        ),
        child: BottomNavigationBar(
          currentIndex: _tab,
          onTap: _onTab,
          items: [
            // Alerts tab — badge for unread standard alerts
            BottomNavigationBarItem(
              icon: _BadgeIcon(
                icon: Icons.shield_outlined,
                count: _alertUnread,
              ),
              activeIcon: _BadgeIcon(
                icon: Icons.shield,
                count: _alertUnread,
              ),
              label: 'Alerts',
            ),
            // Notifications tab — badge for unread emergency escalations
            BottomNavigationBarItem(
              icon: _BadgeIcon(
                icon: Icons.notifications_outlined,
                count: _emergencyUnread,
                color: const Color(0xFFef4444),
              ),
              activeIcon: _BadgeIcon(
                icon: Icons.notifications,
                count: _emergencyUnread,
                color: const Color(0xFFef4444),
              ),
              label: 'Notifications',
            ),
            const BottomNavigationBarItem(
              icon: Icon(Icons.settings_outlined),
              activeIcon: Icon(Icons.settings),
              label: 'Settings',
            ),
          ],
        ),
      ),
    );
  }
}

// ── Badge icon widget ─────────────────────────────────────────────────────────
class _BadgeIcon extends StatelessWidget {
  final IconData icon;
  final int count;
  final Color color;
  const _BadgeIcon({
    required this.icon,
    required this.count,
    this.color = const Color(0xFFef4444),
  });

  @override
  Widget build(BuildContext context) {
    return Stack(clipBehavior: Clip.none, children: [
      Icon(icon),
      if (count > 0)
        Positioned(
          right: -6, top: -4,
          child: Container(
            padding: const EdgeInsets.all(3),
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
            child: Text(
              count > 99 ? '99+' : '$count',
              style: const TextStyle(color: Colors.white, fontSize: 9,
                  fontWeight: FontWeight.w700),
            ),
          ),
        ),
    ]);
  }
}

// ── Connection chip ───────────────────────────────────────────────────────────
class _ConnChip extends StatelessWidget {
  final HubConnectionState state;
  final VoidCallback onTap;
  const _ConnChip({required this.state, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final (color, label, spin) = switch (state) {
      HubConnectionState.connected    => (const Color(0xFF22c55e), 'Live',         false),
      HubConnectionState.connecting   => (const Color(0xFFeab308), 'Connecting',   true),
      HubConnectionState.reconnecting => (const Color(0xFFeab308), 'Reconnecting', true),
      HubConnectionState.disconnected => (const Color(0xFFef4444), 'Offline',      false),
    };

    return GestureDetector(
      onTap: state == HubConnectionState.disconnected ? onTap : null,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
        decoration: BoxDecoration(
          color: color.withOpacity(0.12),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: color.withOpacity(0.35)),
        ),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          if (spin)
            SizedBox(width: 9, height: 9,
                child: CircularProgressIndicator(strokeWidth: 1.5, color: color))
          else
            Container(width: 7, height: 7,
                decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
          const SizedBox(width: 5),
          Text(label, style: TextStyle(color: color, fontSize: 11,
              fontWeight: FontWeight.w600)),
        ]),
      ),
    );
  }
}

// ── Emergency popup dialog ────────────────────────────────────────────────────
class _EmergencyDialog extends StatelessWidget {
  final EmergencyNotificationModel emergency;
  const _EmergencyDialog({required this.emergency});

  @override
  Widget build(BuildContext context) {
    return Dialog(
      backgroundColor: const Color(0xFF111827),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: Color(0xFFef4444), width: 2),
      ),
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          // Header
          Row(children: [
            const Text('🚨', style: TextStyle(fontSize: 28)),
            const SizedBox(width: 12),
            Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Text('EMERGENCY ESCALATED',
                  style: TextStyle(color: Color(0xFFef4444), fontSize: 13,
                      fontWeight: FontWeight.w800, letterSpacing: 1)),
              Text(emergency.type,
                  style: const TextStyle(color: Colors.white, fontSize: 16,
                      fontWeight: FontWeight.w700)),
            ])),
          ]),
          const SizedBox(height: 16),
          const Divider(color: Color(0xFF1f2937)),
          const SizedBox(height: 12),
          // Details
          _Row('Camera',  emergency.cameraName),
          _Row('Message', emergency.message),
          _Row('By',      emergency.escalatedBy),
          _Row('Time',    _fmt(emergency.escalatedAt)),
          const SizedBox(height: 20),
          SizedBox(
            width: double.infinity,
            child: ElevatedButton(
              style: ElevatedButton.styleFrom(
                backgroundColor: const Color(0xFFef4444),
                padding: const EdgeInsets.symmetric(vertical: 14),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
              ),
              onPressed: () => Navigator.pop(context),
              child: const Text('Acknowledged',
                  style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700)),
            ),
          ),
        ]),
      ),
    );
  }

  String _fmt(DateTime dt) {
    final h = dt.hour.toString().padLeft(2, '0');
    final m = dt.minute.toString().padLeft(2, '0');
    return '${dt.day}/${dt.month}/${dt.year}  $h:$m';
  }
}

class _Row extends StatelessWidget {
  final String label;
  final String value;
  const _Row(this.label, this.value);

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 5),
    child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
      SizedBox(width: 70,
          child: Text(label,
              style: const TextStyle(color: Color(0xFF6b7280), fontSize: 12,
                  fontWeight: FontWeight.w600))),
      Expanded(child: Text(value,
          style: const TextStyle(color: Color(0xFF9ca3af), fontSize: 13))),
    ]),
  );
}
