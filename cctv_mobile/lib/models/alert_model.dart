/// Represents a real-time alert received from the CCTV Guard backend via SignalR.
class AlertModel {
  final String id;
  final String type;
  final String message;
  final String cameraName;
  final String severity;
  final DateTime timestamp;
  bool isRead;

  AlertModel({
    required this.id,
    required this.type,
    required this.message,
    required this.cameraName,
    required this.severity,
    required this.timestamp,
    this.isRead = false,
  });

  factory AlertModel.fromJson(Map<String, dynamic> json) {
    return AlertModel(
      id:         json['id']         as String? ?? '',
      type:       json['type']       as String? ?? 'Alert',
      message:    json['message']    as String? ?? '',
      cameraName: json['cameraName'] as String? ?? 'Unknown Camera',
      severity:   json['severity']   as String? ?? 'low',
      timestamp:  json['timestamp'] != null
          ? DateTime.parse(json['timestamp'].toString()).toLocal()
          : DateTime.now(),
    );
  }

  /// Emoji icon based on alert type
  String get icon {
    switch (type.toLowerCase()) {
      case 'weapon detected':  return '🔫';
      case 'fight detected':   return '🥊';
      case 'fire detected':    return '🔥';
      case 'intrusion detected': return '🚨';
      case 'unknown_face detected': return '👤';
      case 'license_plate detected': return '🚗';
      default: return '⚠️';
    }
  }

  /// Color hex string based on severity
  String get severityColor {
    switch (severity.toLowerCase()) {
      case 'critical': return '#ef4444';
      case 'high':     return '#f97316';
      case 'medium':   return '#eab308';
      case 'low':      return '#22c55e';
      default:         return '#6b7280';
    }
  }
}
