/// Represents an emergency escalation pushed from the Angular operator console.
/// Received via SignalR event: ReceiveEmergencyNotification
class EmergencyNotificationModel {
  final String alertId;
  final String incidentId;
  final String type;
  final String message;
  final String cameraName;
  final String severity;
  final DateTime timestamp;
  final String? imageUrl;
  final String escalatedBy;
  final DateTime escalatedAt;

  EmergencyNotificationModel({
    required this.alertId,
    required this.incidentId,
    required this.type,
    required this.message,
    required this.cameraName,
    required this.severity,
    required this.timestamp,
    this.imageUrl,
    required this.escalatedBy,
    required this.escalatedAt,
  });

  factory EmergencyNotificationModel.fromJson(Map<String, dynamic> json) {
    return EmergencyNotificationModel(
      alertId:     json['alertId']     as String? ?? '',
      incidentId:  json['incidentId']  as String? ?? '',
      type:        json['type']        as String? ?? 'Emergency',
      message:     json['message']     as String? ?? '',
      cameraName:  json['cameraName']  as String? ?? 'Unknown Camera',
      severity:    json['severity']    as String? ?? 'critical',
      imageUrl:    json['imageUrl']    as String?,
      escalatedBy: json['escalatedBy'] as String? ?? 'Operator',
      timestamp: json['timestamp'] != null
          ? DateTime.parse(json['timestamp'].toString()).toLocal()
          : DateTime.now(),
      escalatedAt: json['escalatedAt'] != null
          ? DateTime.parse(json['escalatedAt'].toString()).toLocal()
          : DateTime.now(),
    );
  }

  /// Severity color hex
  String get severityColor {
    switch (severity.toLowerCase()) {
      case 'critical': return '#ef4444';
      case 'high':     return '#f97316';
      case 'medium':   return '#eab308';
      default:         return '#22c55e';
    }
  }

  /// Emoji icon for the alert type
  String get icon {
    switch (type.toLowerCase()) {
      case 'weapon detected':        return '🔫';
      case 'fight detected':         return '🥊';
      case 'fire detected':          return '🔥';
      case 'intrusion detected':     return '🚨';
      case 'unknown_face detected':  return '👤';
      case 'license_plate detected': return '🚗';
      default:                       return '🚨';
    }
  }
}
