import 'dart:async';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/alert_model.dart';
import '../services/alert_service.dart';
import '../services/auth_service.dart';

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  final AlertService _alertService = AlertService();
  final List<AlertModel> _alerts   = [];

  late StreamSubscription<HubConnectionState> _stateSub;
  late StreamSubscription<AlertModel>          _alertSub;

  HubConnectionState _connState = HubConnectionState.disconnected;
  bool _connecting = false;

  @override
  void initState() {
    super.initState();

    // Subscribe to connection state changes
    _stateSub = _alertService.connectionState$.listen((state) {
      if (mounted) setState(() => _connState = state);
    });

    // Subscribe to incoming alerts
    _alertSub = _alertService.alerts$.listen((alert) {
      if (mounted) {
        setState(() {
          _alerts.insert(0, alert); // newest first
          if (_alerts.length > 100) _alerts.removeLast(); // keep last 100
        });
      }
    });

    // Set initial state
    _connState = _alertService.currentState;

    // Auto-connect if not already connected
    if (_connState == HubConnectionState.disconnected) {
      _connect();
    }
  }

  @override
  void dispose() {
    _stateSub.cancel();
    _alertSub.cancel();
    super.dispose();
  }

  Future<void> _connect() async {
    setState(() => _connecting = true);
    try {
      await _alertService.connect();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Connection failed: ${e.toString().substring(0, 80)}'),
            backgroundColor: const Color(0xFFef4444),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _connecting = false);
    }
  }

  Future<void> _logout() async {
    await AuthService().logout();
    if (mounted) Navigator.pushReplacementNamed(context, '/login');
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            Container(
              width: 32, height: 32,
              decoration: BoxDecoration(
                color: const Color(0xFF3b82f6),
                borderRadius: BorderRadius.circular(8),
              ),
              child: const Icon(Icons.videocam, color: Colors.white, size: 18),
            ),
            const SizedBox(width: 10),
            const Text('CCTV Guard'),
          ],
        ),
        actions: [
          // Connection status chip
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 4),
            child: _ConnectionChip(state: _connState, onReconnect: _connect),
          ),
          // Logout
          IconButton(
            icon: const Icon(Icons.logout, color: Color(0xFF9ca3af)),
            tooltip: 'Logout',
            onPressed: _logout,
          ),
        ],
      ),
      body: Column(
        children: [
          // Stats bar
          _StatsBar(alertCount: _alerts.length, criticalCount: _alerts.where((a) => a.severity == 'critical').length),

          // Alert list header
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
            child: Row(
              children: [
                const Text('Recent Alerts',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: Colors.white)),
                const Spacer(),
                if (_alerts.isNotEmpty)
                  TextButton(
                    onPressed: () => setState(() => _alerts.clear()),
                    child: const Text('Clear', style: TextStyle(color: Color(0xFF6b7280), fontSize: 13)),
                  ),
              ],
            ),
          ),

          // Alert list
          Expanded(
            child: _alerts.isEmpty
                ? _EmptyState(connState: _connState, onConnect: _connect, connecting: _connecting)
                : ListView.builder(
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                    itemCount: _alerts.length,
                    itemBuilder: (ctx, i) => _AlertCard(alert: _alerts[i]),
                  ),
          ),
        ],
      ),
    );
  }
}

// ── Connection status chip ────────────────────────────────────────────────────

class _ConnectionChip extends StatelessWidget {
  final HubConnectionState state;
  final VoidCallback onReconnect;
  const _ConnectionChip({required this.state, required this.onReconnect});

  @override
  Widget build(BuildContext context) {
    Color color;
    String label;
    bool showDot = false;

    switch (state) {
      case HubConnectionState.connected:
        color = const Color(0xFF22c55e); label = 'Online'; showDot = true;
      case HubConnectionState.connecting:
      case HubConnectionState.reconnecting:
        color = const Color(0xFFeab308); label = 'Connecting...';
      case HubConnectionState.disconnected:
        color = const Color(0xFFef4444); label = 'Offline';
    }

    return GestureDetector(
      onTap: state == HubConnectionState.disconnected ? onReconnect : null,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
        decoration: BoxDecoration(
          color: color.withOpacity(0.15),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: color.withOpacity(0.4)),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (showDot) ...[
              Container(
                width: 7, height: 7,
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
              ),
              const SizedBox(width: 5),
            ] else if (state != HubConnectionState.disconnected) ...[
              SizedBox(
                width: 10, height: 10,
                child: CircularProgressIndicator(strokeWidth: 1.5, color: color),
              ),
              const SizedBox(width: 5),
            ],
            Text(label, style: TextStyle(color: color, fontSize: 12, fontWeight: FontWeight.w600)),
          ],
        ),
      ),
    );
  }
}

// ── Stats bar ─────────────────────────────────────────────────────────────────

class _StatsBar extends StatelessWidget {
  final int alertCount;
  final int criticalCount;
  const _StatsBar({required this.alertCount, required this.criticalCount});

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.all(16),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: const Color(0xFF1f2937)),
      ),
      child: Row(
        children: [
          _StatItem(label: 'Session Alerts', value: alertCount.toString(), color: const Color(0xFF3b82f6)),
          _divider(),
          _StatItem(label: 'Critical', value: criticalCount.toString(), color: const Color(0xFFef4444)),
          _divider(),
          _StatItem(
            label: 'High',
            value: (alertCount - criticalCount).toString(),
            color: const Color(0xFFf97316),
          ),
        ],
      ),
    );
  }

  Widget _divider() => Container(
    width: 1, height: 36, margin: const EdgeInsets.symmetric(horizontal: 16),
    color: const Color(0xFF1f2937),
  );
}

class _StatItem extends StatelessWidget {
  final String label;
  final String value;
  final Color color;
  const _StatItem({required this.label, required this.value, required this.color});

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Column(
        children: [
          Text(value, style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: color)),
          const SizedBox(height: 2),
          Text(label, style: const TextStyle(fontSize: 11, color: Color(0xFF6b7280))),
        ],
      ),
    );
  }
}

// ── Alert card ────────────────────────────────────────────────────────────────

class _AlertCard extends StatelessWidget {
  final AlertModel alert;
  const _AlertCard({required this.alert});

  @override
  Widget build(BuildContext context) {
    final color = Color(int.parse(alert.severityColor.replaceFirst('#', '0xFF')));
    final timeStr = DateFormat('HH:mm:ss').format(alert.timestamp);
    final dateStr = DateFormat('dd MMM').format(alert.timestamp);

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: const Color(0xFF1f2937)),
        // Left accent bar by severity
      ),
      child: IntrinsicHeight(
        child: Row(
          children: [
            // Severity accent bar
            Container(
              width: 4,
              decoration: BoxDecoration(
                color: color,
                borderRadius: const BorderRadius.only(
                  topLeft: Radius.circular(12),
                  bottomLeft: Radius.circular(12),
                ),
              ),
            ),
            // Content
            Expanded(
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Text(alert.icon, style: const TextStyle(fontSize: 18)),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            alert.type,
                            style: const TextStyle(
                              fontSize: 15, fontWeight: FontWeight.w700, color: Colors.white),
                          ),
                        ),
                        // Severity badge
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                          decoration: BoxDecoration(
                            color: color.withOpacity(0.15),
                            borderRadius: BorderRadius.circular(20),
                            border: Border.all(color: color.withOpacity(0.4)),
                          ),
                          child: Text(
                            alert.severity.toUpperCase(),
                            style: TextStyle(color: color, fontSize: 10, fontWeight: FontWeight.w700),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(alert.message,
                      style: const TextStyle(color: Color(0xFF9ca3af), fontSize: 13)),
                    const SizedBox(height: 8),
                    Row(
                      children: [
                        const Icon(Icons.videocam_outlined, size: 13, color: Color(0xFF6b7280)),
                        const SizedBox(width: 4),
                        Text(alert.cameraName,
                          style: const TextStyle(color: Color(0xFF6b7280), fontSize: 12)),
                        const Spacer(),
                        Text('$dateStr  $timeStr',
                          style: const TextStyle(color: Color(0xFF4b5563), fontSize: 11)),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ── Empty state ───────────────────────────────────────────────────────────────

class _EmptyState extends StatelessWidget {
  final HubConnectionState connState;
  final VoidCallback onConnect;
  final bool connecting;
  const _EmptyState({required this.connState, required this.onConnect, required this.connecting});

  @override
  Widget build(BuildContext context) {
    final isOffline = connState == HubConnectionState.disconnected;

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              isOffline ? Icons.wifi_off : Icons.notifications_none,
              size: 56,
              color: const Color(0xFF374151),
            ),
            const SizedBox(height: 16),
            Text(
              isOffline ? 'Not Connected' : 'No Alerts Yet',
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, color: Color(0xFF6b7280)),
            ),
            const SizedBox(height: 8),
            Text(
              isOffline
                  ? 'Tap below to connect to the CCTV Guard server'
                  : 'Alerts will appear here in real-time when threats are detected',
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 14, color: Color(0xFF4b5563)),
            ),
            if (isOffline) ...[
              const SizedBox(height: 24),
              ElevatedButton.icon(
                onPressed: connecting ? null : onConnect,
                icon: connecting
                    ? const SizedBox(width: 16, height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : const Icon(Icons.wifi, size: 18),
                label: Text(connecting ? 'Connecting...' : 'Connect to Server'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
