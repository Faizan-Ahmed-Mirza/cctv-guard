import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/alert_model.dart';
import '../services/alert_service.dart';

class AlertsTab extends StatefulWidget {
  final List<AlertModel> liveAlerts;
  final HubConnectionState connState;
  final VoidCallback onConnect;
  final VoidCallback onClear;

  const AlertsTab({
    super.key,
    required this.liveAlerts,
    required this.connState,
    required this.onConnect,
    required this.onClear,
  });

  @override
  State<AlertsTab> createState() => _AlertsTabState();
}

class _AlertsTabState extends State<AlertsTab> {
  List<AlertModel> _history = [];
  bool _loadingHistory = false;
  bool _historyLoaded  = false;
  String _filter = 'all'; // all | critical | high | medium | low

  @override
  void initState() {
    super.initState();
    _loadHistory();
  }

  Future<void> _loadHistory() async {
    setState(() => _loadingHistory = true);
    final fetched = await AlertService().fetchAlerts();
    if (mounted) {
      setState(() {
        _history       = fetched;
        _loadingHistory = false;
        _historyLoaded  = true;
      });
    }
  }

  List<AlertModel> get _allAlerts {
    // Merge live + history, deduplicate by id, live first
    final seen = <String>{};
    final merged = <AlertModel>[];
    for (final a in [...widget.liveAlerts, ..._history]) {
      if (seen.add(a.id.isEmpty ? a.timestamp.toString() : a.id)) {
        merged.add(a);
      }
    }
    if (_filter == 'all') return merged;
    return merged.where((a) => a.severity == _filter).toList();
  }

  @override
  Widget build(BuildContext context) {
    final alerts = _allAlerts;

    return Column(children: [
      // Stats bar
      _StatsBar(alerts: alerts, connState: widget.connState),

      // Filter chips
      _FilterRow(current: _filter, onChanged: (f) => setState(() => _filter = f)),

      // Header
      Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
        child: Row(children: [
          Text('${alerts.length} alert${alerts.length == 1 ? '' : 's'}',
              style: const TextStyle(fontSize: 13, color: Color(0xFF6b7280))),
          const Spacer(),
          if (_loadingHistory)
            const SizedBox(width: 14, height: 14,
                child: CircularProgressIndicator(strokeWidth: 1.5,
                    color: Color(0xFF3b82f6))),
          if (!_loadingHistory && _historyLoaded)
            GestureDetector(
              onTap: _loadHistory,
              child: const Icon(Icons.refresh, size: 18, color: Color(0xFF6b7280)),
            ),
          if (widget.liveAlerts.isNotEmpty) ...[
            const SizedBox(width: 12),
            GestureDetector(
              onTap: widget.onClear,
              child: const Text('Clear live',
                  style: TextStyle(color: Color(0xFF6b7280), fontSize: 12)),
            ),
          ],
        ]),
      ),

      // List
      Expanded(
        child: alerts.isEmpty
            ? _EmptyState(connState: widget.connState, onConnect: widget.onConnect,
                loading: _loadingHistory)
            : RefreshIndicator(
                onRefresh: _loadHistory,
                color: const Color(0xFF3b82f6),
                child: ListView.builder(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
                  itemCount: alerts.length,
                  itemBuilder: (_, i) => _AlertCard(alert: alerts[i]),
                ),
              ),
      ),
    ]);
  }
}

// ── Stats bar ─────────────────────────────────────────────────────────────────
class _StatsBar extends StatelessWidget {
  final List<AlertModel> alerts;
  final HubConnectionState connState;
  const _StatsBar({required this.alerts, required this.connState});

  @override
  Widget build(BuildContext context) {
    final critical = alerts.where((a) => a.severity.toLowerCase() == 'critical').length;
    final high     = alerts.where((a) => a.severity.toLowerCase() == 'high').length;
    final medium   = alerts.where((a) => a.severity.toLowerCase() == 'medium').length;

    final (dotColor, label) = switch (connState) {
      HubConnectionState.connected    => (const Color(0xFF22c55e), 'Live'),
      HubConnectionState.reconnecting => (const Color(0xFFeab308), 'Reconnecting'),
      HubConnectionState.connecting   => (const Color(0xFFeab308), 'Connecting'),
      HubConnectionState.disconnected => (const Color(0xFFef4444), 'Offline'),
    };

    return Container(
      margin: const EdgeInsets.fromLTRB(16, 14, 16, 0),
      padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 16),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: const Color(0xFF1f2937)),
      ),
      child: Row(children: [
        _Stat('Today', alerts.length.toString(), const Color(0xFF3b82f6)),
        _div(),
        _Stat('Critical', critical.toString(), const Color(0xFFef4444)),
        _div(),
        _Stat('High', high.toString(), const Color(0xFFf97316)),
        _div(),
        _Stat('Medium', medium.toString(), const Color(0xFFeab308)),
        _div(),
        // Connection dot
        Column(children: [
          Row(mainAxisSize: MainAxisSize.min, children: [
            Container(width: 7, height: 7,
                decoration: BoxDecoration(color: dotColor, shape: BoxShape.circle)),
            const SizedBox(width: 4),
            Text(label, style: TextStyle(color: dotColor, fontSize: 10,
                fontWeight: FontWeight.w700)),
          ]),
          const SizedBox(height: 2),
          const Text('Status', style: TextStyle(fontSize: 9, color: Color(0xFF6b7280))),
        ]),
      ]),
    );
  }

  Widget _div() => Container(
      width: 1, height: 30, margin: const EdgeInsets.symmetric(horizontal: 10),
      color: const Color(0xFF1f2937));
}

class _Stat extends StatelessWidget {
  final String label, value;
  final Color color;
  const _Stat(this.label, this.value, this.color);

  @override
  Widget build(BuildContext context) => Expanded(child: Column(children: [
    Text(value, style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: color)),
    const SizedBox(height: 2),
    Text(label, style: const TextStyle(fontSize: 9, color: Color(0xFF6b7280))),
  ]));
}

// ── Filter row ────────────────────────────────────────────────────────────────
class _FilterRow extends StatelessWidget {
  final String current;
  final ValueChanged<String> onChanged;
  const _FilterRow({required this.current, required this.onChanged});

  @override
  Widget build(BuildContext context) {
    const chips = [
      ('All',      'all',      Color(0xFF3b82f6)),
      ('Critical', 'critical', Color(0xFFef4444)),
      ('High',     'high',     Color(0xFFf97316)),
      ('Medium',   'medium',   Color(0xFFeab308)),
      ('Low',      'low',      Color(0xFF22c55e)),
    ];
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: chips.map((c) {
            final active = current == c.$2;
            return Padding(
              padding: const EdgeInsets.only(right: 8),
              child: GestureDetector(
                onTap: () => onChanged(c.$2),
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
                  decoration: BoxDecoration(
                    color: active ? c.$3.withOpacity(0.18) : const Color(0xFF1f2937),
                    borderRadius: BorderRadius.circular(20),
                    border: Border.all(
                        color: active ? c.$3.withOpacity(0.5) : const Color(0xFF374151)),
                  ),
                  child: Text(c.$1,
                      style: TextStyle(
                          color: active ? c.$3 : const Color(0xFF9ca3af),
                          fontSize: 12, fontWeight: FontWeight.w600)),
                ),
              ),
            );
          }).toList(),
        ),
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
    final time  = DateFormat('HH:mm:ss  dd MMM').format(alert.timestamp);

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: const Color(0xFF1f2937)),
      ),
      child: IntrinsicHeight(
        child: Row(children: [
          // Severity bar
          Container(width: 4,
              decoration: BoxDecoration(color: color,
                  borderRadius: const BorderRadius.only(
                      topLeft: Radius.circular(12),
                      bottomLeft: Radius.circular(12)))),
          Expanded(child: Padding(
            padding: const EdgeInsets.all(13),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Text(alert.icon, style: const TextStyle(fontSize: 17)),
                const SizedBox(width: 8),
                Expanded(child: Text(alert.type,
                    style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700,
                        color: Colors.white))),
                _Badge(label: alert.severity.toUpperCase(), color: color),
              ]),
              const SizedBox(height: 5),
              Text(alert.message,
                  style: const TextStyle(color: Color(0xFF9ca3af), fontSize: 12)),
              const SizedBox(height: 10),
              Row(children: [
                const Icon(Icons.videocam_outlined, size: 12, color: Color(0xFF6b7280)),
                const SizedBox(width: 4),
                Expanded(child: Text(alert.cameraName,
                    style: const TextStyle(color: Color(0xFF6b7280), fontSize: 11),
                    overflow: TextOverflow.ellipsis)),
                Text(time, style: const TextStyle(color: Color(0xFF4b5563), fontSize: 10)),
              ]),
            ]),
          )),
        ]),
      ),
    );
  }
}

class _Badge extends StatelessWidget {
  final String label;
  final Color color;
  const _Badge({required this.label, required this.color});

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
    decoration: BoxDecoration(
      color: color.withOpacity(0.12),
      borderRadius: BorderRadius.circular(20),
      border: Border.all(color: color.withOpacity(0.35)),
    ),
    child: Text(label,
        style: TextStyle(color: color, fontSize: 9, fontWeight: FontWeight.w700)),
  );
}

// ── Empty state ───────────────────────────────────────────────────────────────
class _EmptyState extends StatelessWidget {
  final HubConnectionState connState;
  final VoidCallback onConnect;
  final bool loading;
  const _EmptyState({required this.connState, required this.onConnect,
      required this.loading});

  @override
  Widget build(BuildContext context) {
    if (loading) {
      return const Center(child: CircularProgressIndicator(color: Color(0xFF3b82f6)));
    }
    final offline = connState == HubConnectionState.disconnected;
    return Center(child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
        Icon(offline ? Icons.wifi_off_rounded : Icons.notifications_none_rounded,
            size: 64, color: const Color(0xFF374151)),
        const SizedBox(height: 16),
        Text(offline ? 'Not Connected' : 'No Alerts',
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700,
                color: Color(0xFF6b7280))),
        const SizedBox(height: 8),
        Text(
          offline
              ? 'Tap Reconnect to connect to the server'
              : 'Alerts appear here in real-time when threats are detected',
          textAlign: TextAlign.center,
          style: const TextStyle(fontSize: 13, color: Color(0xFF4b5563))),
        if (offline) ...[
          const SizedBox(height: 24),
          ElevatedButton.icon(
            onPressed: onConnect,
            icon: const Icon(Icons.wifi_rounded, size: 18),
            label: const Text('Reconnect'),
          ),
        ],
      ]),
    ));
  }
}
