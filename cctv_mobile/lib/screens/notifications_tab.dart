import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/alert_model.dart';

/// Shows all received alerts (live + history) grouped by date,
/// acting as a notification history / inbox.
class NotificationsTab extends StatelessWidget {
  final List<AlertModel> alerts;
  const NotificationsTab({super.key, required this.alerts});

  @override
  Widget build(BuildContext context) {
    // Only show Critical and High severity alerts in the Notifications tab
    final filteredAlerts = alerts.where((a) =>
      a.severity.toLowerCase() == 'critical' ||
      a.severity.toLowerCase() == 'high'
    ).toList();

    if (filteredAlerts.isEmpty) {
      return const Center(child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.mark_email_read_outlined, size: 64, color: Color(0xFF374151)),
          SizedBox(height: 16),
          Text('No urgent notifications',
              style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700,
                  color: Color(0xFF6b7280))),
          SizedBox(height: 8),
          Text('Only critical and high priority alerts\nwill appear here.',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 13, color: Color(0xFF4b5563))),
        ],
      ));
    }

    // Group by date
    final grouped = <String, List<AlertModel>>{};
    for (final a in filteredAlerts) {
      final key = DateFormat('EEEE, d MMM yyyy').format(a.timestamp);
      grouped.putIfAbsent(key, () => []).add(a);
    }

    final sections = grouped.entries.toList();

    return ListView.builder(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 24),
      itemCount: sections.length,
      itemBuilder: (_, si) {
        final section = sections[si];
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          // Date header
          Padding(
            padding: EdgeInsets.only(bottom: 10, top: si == 0 ? 0 : 16),
            child: Text(section.key,
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700,
                    color: Color(0xFF6b7280), letterSpacing: 0.5)),
          ),
          ...section.value.map((a) => _NotifTile(alert: a)),
        ]);
      },
    );
  }
}

class _NotifTile extends StatelessWidget {
  final AlertModel alert;
  const _NotifTile({required this.alert});

  @override
  Widget build(BuildContext context) {
    final color = Color(int.parse(alert.severityColor.replaceFirst('#', '0xFF')));
    final time  = DateFormat('HH:mm').format(alert.timestamp);

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(13),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withOpacity(0.25)),
      ),
      child: Row(children: [
        // Icon circle
        Container(
          width: 42, height: 42,
          decoration: BoxDecoration(
            color: color.withOpacity(0.14),
            borderRadius: BorderRadius.circular(12),
          ),
          child: Center(child: Text(alert.icon,
              style: const TextStyle(fontSize: 20))),
        ),
        const SizedBox(width: 12),
        // Content
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(alert.type,
                style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700,
                    color: Colors.white))),
            Text(time, style: const TextStyle(fontSize: 11, color: Color(0xFF4b5563))),
          ]),
          const SizedBox(height: 3),
          Text(alert.message,
              style: const TextStyle(fontSize: 12, color: Color(0xFF9ca3af)),
              maxLines: 2, overflow: TextOverflow.ellipsis),
          if (alert.imageUrl != null && alert.imageUrl!.isNotEmpty) ...[
            const SizedBox(height: 8),
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.network(
                alert.imageUrl!,
                height: 120,
                width: double.infinity,
                fit: BoxFit.cover,
                errorBuilder: (_, __, ___) => const SizedBox.shrink(),
              ),
            ),
          ],
          const SizedBox(height: 8),
          Row(children: [
            const Icon(Icons.videocam_outlined, size: 11, color: Color(0xFF6b7280)),
            const SizedBox(width: 3),
            Text(alert.cameraName,
                style: const TextStyle(fontSize: 11, color: Color(0xFF6b7280))),
            const SizedBox(width: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
              decoration: BoxDecoration(
                color: color.withOpacity(0.12),
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: color.withOpacity(0.3)),
              ),
              child: Text(alert.severity.toUpperCase(),
                  style: TextStyle(color: color, fontSize: 9,
                      fontWeight: FontWeight.w700)),
            ),
          ]),
        ])),
      ]),
    );
  }
}
