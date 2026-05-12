import 'dart:async';
import 'package:flutter/material.dart';
import '../services/alert_service.dart';
import '../services/auth_service.dart';
import '../models/alert_model.dart';
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

  final List<AlertModel> _liveAlerts = [];
  int _unread = 0;

  late StreamSubscription<HubConnectionState> _stateSub;
  late StreamSubscription<AlertModel>          _alertSub;
  HubConnectionState _connState = HubConnectionState.disconnected;

  @override
  void initState() {
    super.initState();
    _connState = _svc.currentState;

    _stateSub = _svc.connectionState$.listen((s) {
      if (mounted) setState(() => _connState = s);
    });

    _alertSub = _svc.alerts$.listen((alert) {
      if (mounted) {
        setState(() {
          _liveAlerts.insert(0, alert);
          if (_liveAlerts.length > 300) _liveAlerts.removeLast();
          if (_tab != 0) _unread++;
        });
        _showInAppPopup(alert);
      }
    });

    _fetchHistory();

    if (_connState == HubConnectionState.disconnected) _connect();
  }

  Future<void> _fetchHistory() async {
    final history = await _svc.fetchAlerts();
    if (mounted && history.isNotEmpty) {
      setState(() {
        // Add history but avoid duplicates with live alerts
        final existingIds = _liveAlerts.map((a) => a.id).toSet();
        for (var a in history) {
          if (!existingIds.contains(a.id)) {
            _liveAlerts.add(a);
          }
        }
        // Sort newest first
        _liveAlerts.sort((a, b) => b.timestamp.compareTo(a.timestamp));
      });
    }
  }

  @override
  void dispose() {
    _stateSub.cancel();
    _alertSub.cancel();
    super.dispose();
  }

  Future<void> _connect() async {
    try { await _svc.connect(); } catch (_) {}
  }

  void _showInAppPopup(AlertModel alert) {
    // Only show popup if not already on the alerts tab or if it's high severity
    if (_tab == 0 && alert.severity != 'critical') return;

    final color = Color(int.parse(alert.severityColor.replaceFirst('#', '0xFF')));

    ScaffoldMessenger.of(context).hideCurrentSnackBar();
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        backgroundColor: const Color(0xFF111827),
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
          side: BorderSide(color: color.withOpacity(0.5)),
        ),
        margin: const EdgeInsets.all(12),
        duration: const Duration(seconds: 5),
        content: Row(
          children: [
            Text(alert.icon, style: const TextStyle(fontSize: 22)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(alert.type,
                      style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.white)),
                  Text(alert.message,
                      style: const TextStyle(fontSize: 12, color: Color(0xFF9ca3af)),
                      maxLines: 1, overflow: TextOverflow.ellipsis),
                ],
              ),
            ),
            TextButton(
              onPressed: () {
                ScaffoldMessenger.of(context).hideCurrentSnackBar();
                _onTab(0);
              },
              child: Text('VIEW', style: TextStyle(color: color, fontWeight: FontWeight.bold)),
            ),
          ],
        ),
      ),
    );
  }

  void _onTab(int i) => setState(() {
    _tab = i;
    if (i == 0) _unread = 0;
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
            onClear: () => setState(() { _liveAlerts.clear(); _unread = 0; }),
          ),
          NotificationsTab(alerts: _liveAlerts),
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
            BottomNavigationBarItem(
              icon: Stack(clipBehavior: Clip.none, children: [
                const Icon(Icons.shield_outlined),
                if (_unread > 0)
                  Positioned(
                    right: -6, top: -4,
                    child: Container(
                      padding: const EdgeInsets.all(3),
                      decoration: const BoxDecoration(
                          color: Color(0xFFef4444), shape: BoxShape.circle),
                      child: Text('$_unread',
                          style: const TextStyle(color: Colors.white, fontSize: 9,
                              fontWeight: FontWeight.w700)),
                    ),
                  ),
              ]),
              activeIcon: const Icon(Icons.shield),
              label: 'Alerts',
            ),
            const BottomNavigationBarItem(
              icon: Icon(Icons.notifications_outlined),
              activeIcon: Icon(Icons.notifications),
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
