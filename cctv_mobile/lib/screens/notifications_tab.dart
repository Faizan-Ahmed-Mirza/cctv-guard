import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/emergency_notification_model.dart';

/// Notifications tab — shows ONLY operator-escalated emergency alerts.
/// Standard AI-generated alerts go to the Alerts tab.
/// This tab is populated exclusively by ReceiveEmergencyNotification SignalR events.
class NotificationsTab extends StatelessWidget {
  final List<EmergencyNotificationModel> emergencies;
  const NotificationsTab({super.key, required this.emergencies});

  @override
  Widget build(BuildContext context) {
    if (emergencies.isEmpty) {
      return const Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.notifications_off_outlined, size: 64, color: Color(0xFF374151)),
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

    // Group by date
    final grouped = <String, List<EmergencyNotificationModel>>{};
    for (final e in emergencies) {
      final key = DateFormat('EEEE, d MMM yyyy').format(e.escalatedAt);
      grouped.putIfAbsent(key, () => []).add(e);
    }
    final sections = grouped.entries.toList();

    return ListView.builder(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 24),
      itemCount: sections.length,
      itemBuilder: (_, si) {
        final section = sections[si];
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Padding(
            padding: EdgeInsets.only(bottom: 10, top: si == 0 ? 0 : 16),
            child: Text(section.key,
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700,
                    color: Color(0xFF6b7280), letterSpacing: 0.5)),
          ),
          ...section.value.map((e) => _EmergencyTile(emergency: e)),
        ]);
      },
    );
  }
}

class _EmergencyTile extends StatelessWidget {
  final EmergencyNotificationModel emergency;
  const _EmergencyTile({required this.emergency});

  @override
  Widget build(BuildContext context) {
    final color = Color(int.parse(emergency.severityColor.replaceFirst('#', '0xFF')));
    final time  = DateFormat('HH:mm').format(emergency.escalatedAt);

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      decoration: BoxDecoration(
        color: const Color(0xFF111827),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withOpacity(0.4), width: 1.5),
        boxShadow: [
          BoxShadow(color: color.withOpacity(0.08), blurRadius: 8, offset: const Offset(0, 2)),
        ],
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        // Emergency header bar
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 8),
          decoration: BoxDecoration(
            color: color.withOpacity(0.12),
            borderRadius: const BorderRadius.only(
              topLeft: Radius.circular(11), topRight: Radius.circular(11),
            ),
          ),
          child: Row(children: [
            const Text('🚨', style: TextStyle(fontSize: 14)),
            const SizedBox(width: 6),
            Text('EMERGENCY ESCALATED',
                style: TextStyle(color: color, fontSize: 10,
                    fontWeight: FontWeight.w800, letterSpacing: 1)),
            const Spacer(),
            Text(time, style: const TextStyle(fontSize: 11, color: Color(0xFF4b5563))),
          ]),
        ),

        // Content
        Padding(
          padding: const EdgeInsets.all(13),
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            // Icon
            Container(
              width: 44, height: 44,
              decoration: BoxDecoration(
                color: color.withOpacity(0.14),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Center(child: Text(emergency.icon,
                  style: const TextStyle(fontSize: 22))),
            ),
            const SizedBox(width: 12),
            // Details
            Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(emergency.type,
                  style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700,
                      color: Colors.white)),
              const SizedBox(height: 3),
              Text(emergency.message,
                  style: const TextStyle(fontSize: 12, color: Color(0xFF9ca3af)),
                  maxLines: 2, overflow: TextOverflow.ellipsis),
              const SizedBox(height: 8),
              // Camera + severity + escalated by
              Wrap(spacing: 8, runSpacing: 4, children: [
                Row(mainAxisSize: MainAxisSize.min, children: [
                  const Icon(Icons.videocam_outlined, size: 11, color: Color(0xFF6b7280)),
                  const SizedBox(width: 3),
                  Text(emergency.cameraName,
                      style: const TextStyle(fontSize: 11, color: Color(0xFF6b7280))),
                ]),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                  decoration: BoxDecoration(
                    color: color.withOpacity(0.12),
                    borderRadius: BorderRadius.circular(20),
                    border: Border.all(color: color.withOpacity(0.3)),
                  ),
                  child: Text(emergency.severity.toUpperCase(),
                      style: TextStyle(color: color, fontSize: 9,
                          fontWeight: FontWeight.w700)),
                ),
                Row(mainAxisSize: MainAxisSize.min, children: [
                  const Icon(Icons.person_outline, size: 11, color: Color(0xFF6b7280)),
                  const SizedBox(width: 3),
                  Text('by ${emergency.escalatedBy}',
                      style: const TextStyle(fontSize: 11, color: Color(0xFF6b7280))),
                ]),
              ]),
            ])),
          ]),
        ),

        // Thumbnail image if available
        if (emergency.imageUrl != null && emergency.imageUrl!.isNotEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(13, 0, 13, 13),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.network(
                emergency.imageUrl!,
                height: 140,
                width: double.infinity,
                fit: BoxFit.cover,
                errorBuilder: (_, __, ___) => const SizedBox.shrink(),
              ),
            ),
          ),
      ]),
    );
  }
}
