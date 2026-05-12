import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/emergency_notification_model.dart';
import '../services/alert_service.dart';

/// Notifications tab — shows ONLY operator-escalated emergency alerts.
/// Standard AI-generated alerts go to the Alerts tab.
class NotificationsTab extends StatefulWidget {
  final List<EmergencyNotificationModel> emergencies;
  final VoidCallback onReload;

  const NotificationsTab({
    super.key,
    required this.emergencies,
    required this.onReload,
  });

  @override
  State<NotificationsTab> createState() => _NotificationsTabState();
}

class _NotificationsTabState extends State<NotificationsTab> {
  // Track which incidents are being acknowledged (show spinner)
  final Set<String> _acknowledging = {};

  Future<void> _acknowledge(EmergencyNotificationModel emergency) async {
    if (_acknowledging.contains(emergency.incidentId)) return;
    setState(() => _acknowledging.add(emergency.incidentId));

    final ok = await AlertService().acknowledgeIncident(emergency.incidentId);

    if (mounted) {
      setState(() => _acknowledging.remove(emergency.incidentId));
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(ok
              ? 'Incident acknowledged'
              : 'Failed to acknowledge — check connection'),
          backgroundColor:
              ok ? const Color(0xFF22c55e) : const Color(0xFFef4444),
          duration: const Duration(seconds: 2),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Column(children: [
      // ── Header bar with reload button ─────────────────────────────────
      Container(
        padding: const EdgeInsets.fromLTRB(16, 12, 12, 8),
        child: Row(children: [
          Text(
            '${widget.emergencies.length} emergency notification'
            '${widget.emergencies.length == 1 ? '' : 's'}',
            style: const TextStyle(
                fontSize: 13, color: Color(0xFF6b7280)),
          ),
          const Spacer(),
          IconButton(
            icon: const Icon(Icons.refresh_rounded,
                color: Color(0xFF6b7280), size: 20),
            tooltip: 'Reload',
            onPressed: widget.onReload,
            padding: EdgeInsets.zero,
            constraints: const BoxConstraints(minWidth: 36, minHeight: 36),
          ),
        ]),
      ),

      // ── List ──────────────────────────────────────────────────────────
      Expanded(
        child: widget.emergencies.isEmpty
            ? const _EmptyState()
            : _buildList(),
      ),
    ]);
  }

  Widget _buildList() {
    // Group by date
    final grouped = <String, List<EmergencyNotificationModel>>{};
    for (final e in widget.emergencies) {
      final key = DateFormat('EEEE, d MMM yyyy').format(e.escalatedAt);
      grouped.putIfAbsent(key, () => []).add(e);
    }
    final sections = grouped.entries.toList();

    return ListView.builder(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
      itemCount: sections.length,
      itemBuilder: (_, si) {
        final section = sections[si];
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Padding(
            padding: EdgeInsets.only(bottom: 10, top: si == 0 ? 0 : 16),
            child: Text(section.key,
                style: const TextStyle(
                    fontSize: 12, fontWeight: FontWeight.w700,
                    color: Color(0xFF6b7280), letterSpacing: 0.5)),
          ),
          ...section.value.map((e) => _EmergencyTile(
            emergency: e,
            acknowledging: _acknowledging.contains(e.incidentId),
            onAcknowledge: () => _acknowledge(e),
          )),
        ]);
      },
    );
  }
}

// ── Empty state ───────────────────────────────────────────────────────────────
class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) => const Center(
    child: Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(Icons.notifications_off_outlined,
            size: 64, color: Color(0xFF374151)),
        SizedBox(height: 16),
        Text('No Emergency Notifications',
            style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700,
                color: Color(0xFF6b7280))),
        SizedBox(height: 8),
        Padding(
          padding: EdgeInsets.symmetric(horizontal: 32),
          child: Text(
            'This tab shows alerts escalated to emergency services by an operator.\n'
            'Standard AI alerts appear in the Alerts tab.',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 13, color: Color(0xFF4b5563)),
          ),
        ),
      ],
    ),
  );
}

// ── Emergency tile ────────────────────────────────────────────────────────────
class _EmergencyTile extends StatelessWidget {
  final EmergencyNotificationModel emergency;
  final bool acknowledging;
  final VoidCallback onAcknowledge;

  const _EmergencyTile({
    required this.emergency,
    required this.acknowledging,
    required this.onAcknowledge,
  });

  @override
  Widget build(BuildContext context) {
    final color =
        Color(int.parse(emergency.severityColor.replaceFirst('#', '0xFF')));
    final time = DateFormat('HH:mm').format(emergency.escalatedAt);
    final isAcknowledged = emergency.incidentStatus == 'acknowledged' ||
        emergency.incidentStatus == 'resolved';

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
            color: isAcknowledged
                ? const Color(0xFF22c55e).withOpacity(0.3)
                : color.withOpacity(0.4),
            width: 1.5),
        boxShadow: [
          BoxShadow(
              color: color.withOpacity(0.06),
              blurRadius: 8,
              offset: const Offset(0, 2)),
        ],
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        // ── Header bar ──────────────────────────────────────────────────
        Container(
          padding:
              const EdgeInsets.symmetric(horizontal: 13, vertical: 8),
          decoration: BoxDecoration(
            color: isAcknowledged
                ? const Color(0xFF22c55e).withOpacity(0.08)
                : color.withOpacity(0.12),
            borderRadius: const BorderRadius.only(
              topLeft: Radius.circular(11),
              topRight: Radius.circular(11),
            ),
          ),
          child: Row(children: [
            Text(isAcknowledged ? '✅' : '🚨',
                style: const TextStyle(fontSize: 14)),
            const SizedBox(width: 6),
            Text(
              isAcknowledged ? 'ACKNOWLEDGED' : 'EMERGENCY ESCALATED',
              style: TextStyle(
                  color: isAcknowledged
                      ? const Color(0xFF22c55e)
                      : color,
                  fontSize: 10,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 1),
            ),
            const Spacer(),
            Text(time,
                style: const TextStyle(
                    fontSize: 11, color: Color(0xFF4b5563))),
          ]),
        ),

        // ── Content ─────────────────────────────────────────────────────
        Padding(
          padding: const EdgeInsets.all(13),
          child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
            // Icon
            Container(
              width: 44, height: 44,
              decoration: BoxDecoration(
                color: color.withOpacity(0.14),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Center(
                  child: Text(emergency.icon,
                      style: const TextStyle(fontSize: 22))),
            ),
            const SizedBox(width: 12),
            // Details
            Expanded(
              child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                Text(emergency.type,
                    style: const TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.w700,
                        color: Colors.white)),
                const SizedBox(height: 3),
                Text(emergency.message,
                    style: const TextStyle(
                        fontSize: 12, color: Color(0xFF9ca3af)),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis),
                const SizedBox(height: 8),
                Wrap(spacing: 8, runSpacing: 4, children: [
                  Row(mainAxisSize: MainAxisSize.min, children: [
                    const Icon(Icons.videocam_outlined,
                        size: 11, color: Color(0xFF6b7280)),
                    const SizedBox(width: 3),
                    Text(emergency.cameraName,
                        style: const TextStyle(
                            fontSize: 11, color: Color(0xFF6b7280))),
                  ]),
                  Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 6, vertical: 2),
                    decoration: BoxDecoration(
                      color: color.withOpacity(0.12),
                      borderRadius: BorderRadius.circular(20),
                      border:
                          Border.all(color: color.withOpacity(0.3)),
                    ),
                    child: Text(emergency.severity.toUpperCase(),
                        style: TextStyle(
                            color: color,
                            fontSize: 9,
                            fontWeight: FontWeight.w700)),
                  ),
                  Row(mainAxisSize: MainAxisSize.min, children: [
                    const Icon(Icons.person_outline,
                        size: 11, color: Color(0xFF6b7280)),
                    const SizedBox(width: 3),
                    Text('by ${emergency.escalatedBy}',
                        style: const TextStyle(
                            fontSize: 11, color: Color(0xFF6b7280))),
                  ]),
                ]),
              ]),
            ),
          ]),
        ),

        // ── Thumbnail ───────────────────────────────────────────────────
        if (emergency.imageUrl != null &&
            emergency.imageUrl!.isNotEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(13, 0, 13, 10),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.network(
                emergency.imageUrl!,
                height: 130,
                width: double.infinity,
                fit: BoxFit.cover,
                errorBuilder: (_, __, ___) => const SizedBox.shrink(),
              ),
            ),
          ),

        // ── Acknowledge button ───────────────────────────────────────────
        if (!isAcknowledged)
          Padding(
            padding: const EdgeInsets.fromLTRB(13, 0, 13, 13),
            child: SizedBox(
              width: double.infinity,
              height: 38,
              child: ElevatedButton.icon(
                onPressed: acknowledging ? null : onAcknowledge,
                style: ElevatedButton.styleFrom(
                  backgroundColor: const Color(0xFF1f2937),
                  disabledBackgroundColor: const Color(0xFF111827),
                  shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(8)),
                  elevation: 0,
                ),
                icon: acknowledging
                    ? const SizedBox(
                        width: 14, height: 14,
                        child: CircularProgressIndicator(
                            strokeWidth: 2, color: Colors.white))
                    : const Icon(Icons.check_circle_outline,
                        size: 16, color: Color(0xFF22c55e)),
                label: Text(
                  acknowledging ? 'Acknowledging...' : 'Acknowledge',
                  style: const TextStyle(
                      color: Color(0xFF9ca3af),
                      fontSize: 13,
                      fontWeight: FontWeight.w600),
                ),
              ),
            ),
          )
        else
          Padding(
            padding: const EdgeInsets.fromLTRB(13, 0, 13, 13),
            child: Row(children: [
              const Icon(Icons.check_circle,
                  size: 14, color: Color(0xFF22c55e)),
              const SizedBox(width: 6),
              Text(
                emergency.incidentStatus == 'resolved'
                    ? 'Resolved'
                    : 'Acknowledged',
                style: const TextStyle(
                    color: Color(0xFF22c55e),
                    fontSize: 12,
                    fontWeight: FontWeight.w600),
              ),
            ]),
          ),
      ]),
    );
  }
}
