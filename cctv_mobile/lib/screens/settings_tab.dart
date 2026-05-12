import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/alert_service.dart';
import '../services/auth_service.dart';
import '../services/notification_service.dart';

class SettingsTab extends StatefulWidget {
  const SettingsTab({super.key});
  @override
  State<SettingsTab> createState() => _SettingsTabState();
}

class _SettingsTabState extends State<SettingsTab> {
  final _urlCtrl = TextEditingController();
  bool _saving   = false;
  bool _saved    = false;
  String? _fcmToken;

  @override
  void initState() {
    super.initState();
    _urlCtrl.text = AlertService().baseUrl;
    _loadFcmToken();
  }

  Future<void> _loadFcmToken() async {
    final t = await NotificationService().getFcmToken();
    if (mounted) setState(() => _fcmToken = t);
  }

  Future<void> _saveAndReconnect() async {
    final url = _urlCtrl.text.trim();
    if (url.isEmpty) return;
    setState(() { _saving = true; _saved = false; });

    await AlertService().saveUrl(url);
    await AlertService().disconnect();

    try { await AlertService().connect(); } catch (_) {}

    if (mounted) {
      setState(() { _saving = false; _saved = true; });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Server URL saved. Reconnecting...'),
            backgroundColor: Color(0xFF22c55e), duration: Duration(seconds: 2)));
      Future.delayed(const Duration(seconds: 2), () {
        if (mounted) setState(() => _saved = false);
      });
    }
  }

  Future<void> _logout() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        backgroundColor: const Color(0xFF111827),
        title: const Text('Logout', style: TextStyle(color: Colors.white)),
        content: const Text('Are you sure?',
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
  void dispose() {
    _urlCtrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final username = AuthService().cachedUsername ?? 'User';

    return SingleChildScrollView(
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [

        // ── Profile card ──────────────────────────────────────────────────
        Container(
          padding: const EdgeInsets.all(16),
          decoration: _cardDeco(),
          child: Row(children: [
            Container(
              width: 48, height: 48,
              decoration: BoxDecoration(
                gradient: const LinearGradient(
                  colors: [Color(0xFF3b82f6), Color(0xFF6366f1)],
                  begin: Alignment.topLeft, end: Alignment.bottomRight,
                ),
                borderRadius: BorderRadius.circular(14),
              ),
              child: Center(child: Text(username[0].toUpperCase(),
                  style: const TextStyle(color: Colors.white, fontSize: 20,
                      fontWeight: FontWeight.w800))),
            ),
            const SizedBox(width: 14),
            Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(username,
                  style: const TextStyle(color: Colors.white, fontSize: 16,
                      fontWeight: FontWeight.w700)),
              const SizedBox(height: 3),
              const Text('CCTV Guard Operator',
                  style: TextStyle(color: Color(0xFF6b7280), fontSize: 12)),
            ]),
          ]),
        ),

        const SizedBox(height: 22),

        // ── Server URL ────────────────────────────────────────────────────
        _SectionLabel('Server Configuration'),
        const SizedBox(height: 10),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: _cardDeco(),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('Backend Server URL',
                style: TextStyle(color: Color(0xFF9ca3af), fontSize: 12,
                    fontWeight: FontWeight.w600)),
            const SizedBox(height: 8),
            TextField(
              controller: _urlCtrl,
              style: const TextStyle(color: Colors.white, fontSize: 14),
              decoration: const InputDecoration(
                hintText: 'http://192.168.1.x:7225',
                prefixIcon: Icon(Icons.dns_outlined, color: Color(0xFF6b7280), size: 18),
              ),
              keyboardType: TextInputType.url,
            ),
            const SizedBox(height: 8),
            const Text(
              '⚠️  Use your Wi-Fi IP (run ipconfig → IPv4 under Wi-Fi)',
              style: TextStyle(color: Color(0xFF6b7280), fontSize: 11)),
            const SizedBox(height: 14),
            SizedBox(
              width: double.infinity,
              child: ElevatedButton.icon(
                onPressed: (_saving) ? null : _saveAndReconnect,
                icon: _saving
                    ? const SizedBox(width: 16, height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : Icon(_saved ? Icons.check : Icons.save_outlined, size: 16),
                label: Text(_saving ? 'Saving...' : _saved ? 'Saved!' : 'Save & Reconnect'),
                style: _saved
                    ? ElevatedButton.styleFrom(backgroundColor: const Color(0xFF22c55e))
                    : null,
              ),
            ),
          ]),
        ),

        const SizedBox(height: 22),

        // ── Push Notifications ────────────────────────────────────────────
        _SectionLabel('Push Notifications (FCM)'),
        const SizedBox(height: 10),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: _cardDeco(),
          child: Column(children: [
            Row(children: [
              const Icon(Icons.notifications_active_outlined,
                  color: Color(0xFF3b82f6), size: 18),
              const SizedBox(width: 10),
              const Expanded(child: Text('Firebase Push Notifications',
                  style: TextStyle(color: Colors.white, fontSize: 13))),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: const Color(0xFF22c55e).withOpacity(0.15),
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: const Color(0xFF22c55e).withOpacity(0.4)),
                ),
                child: const Text('ACTIVE',
                    style: TextStyle(color: Color(0xFF22c55e), fontSize: 10,
                        fontWeight: FontWeight.w700)),
              ),
            ]),
            const SizedBox(height: 14),
            const Divider(color: Color(0xFF1f2937)),
            const SizedBox(height: 10),
            const Text('FCM Device Token',
                style: TextStyle(color: Color(0xFF6b7280), fontSize: 11)),
            const SizedBox(height: 6),
            GestureDetector(
              onTap: () {
                if (_fcmToken != null) {
                  Clipboard.setData(ClipboardData(text: _fcmToken!));
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('FCM token copied'),
                        duration: Duration(seconds: 2)));
                }
              },
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: const Color(0xFF0a0a0f),
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: const Color(0xFF374151)),
                ),
                child: Row(children: [
                  Expanded(child: Text(
                    _fcmToken ?? 'Loading...',
                    style: const TextStyle(color: Color(0xFF6b7280), fontSize: 10,
                        fontFamily: 'monospace'),
                    maxLines: 2, overflow: TextOverflow.ellipsis,
                  )),
                  const SizedBox(width: 6),
                  const Icon(Icons.copy, size: 14, color: Color(0xFF6b7280)),
                ]),
              ),
            ),
            const SizedBox(height: 8),
            const Text(
              'This token is sent to the backend on login so the server can push alerts directly to this device.',
              style: TextStyle(color: Color(0xFF4b5563), fontSize: 11)),
          ]),
        ),

        const SizedBox(height: 22),

        // ── Connection Info ───────────────────────────────────────────────
        _SectionLabel('Connection Info'),
        const SizedBox(height: 10),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: _cardDeco(),
          child: Column(children: [
            _InfoRow('Hub URL', '${AlertService().baseUrl}/hubs/alerts'),
            _InfoRow('Protocol', 'WebSocket (SignalR)'),
            _InfoRow('Auth', 'JWT Bearer Token'),
            _InfoRow('Push', 'Firebase Cloud Messaging'),
            _InfoRow('Reconnect', '0s → 2s → 5s → 10s → 30s'),
          ]),
        ),

        const SizedBox(height: 22),

        // ── App Info ──────────────────────────────────────────────────────
        _SectionLabel('About'),
        const SizedBox(height: 10),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: _cardDeco(),
          child: Column(children: [
            _InfoRow('App', 'CCTV Guard Mobile'),
            _InfoRow('Version', '1.0.0'),
            _InfoRow('Backend', '.NET 8 + SignalR'),
            _InfoRow('AI Service', 'Python FastAPI + YOLOv8'),
            _InfoRow('Push', 'Firebase Cloud Messaging (FCM)'),
          ]),
        ),

        const SizedBox(height: 28),

        // ── Logout ────────────────────────────────────────────────────────
        SizedBox(
          width: double.infinity,
          child: OutlinedButton.icon(
            onPressed: _logout,
            icon: const Icon(Icons.logout, size: 18, color: Color(0xFFef4444)),
            label: const Text('Logout', style: TextStyle(color: Color(0xFFef4444))),
            style: OutlinedButton.styleFrom(
              side: const BorderSide(color: Color(0xFFef4444)),
              padding: const EdgeInsets.symmetric(vertical: 14),
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
            ),
          ),
        ),
        const SizedBox(height: 24),
      ]),
    );
  }

  BoxDecoration _cardDeco() => BoxDecoration(
    color: const Color(0xFF111827),
    borderRadius: BorderRadius.circular(14),
    border: Border.all(color: const Color(0xFF1f2937)),
  );
}

class _SectionLabel extends StatelessWidget {
  final String title;
  const _SectionLabel(this.title);
  @override
  Widget build(BuildContext context) => Text(title,
      style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700,
          color: Color(0xFF6b7280), letterSpacing: 0.8));
}

class _InfoRow extends StatelessWidget {
  final String label, value;
  const _InfoRow(this.label, this.value);
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 6),
    child: Row(children: [
      SizedBox(width: 110,
          child: Text(label,
              style: const TextStyle(color: Color(0xFF6b7280), fontSize: 12))),
      Expanded(child: Text(value,
          style: const TextStyle(color: Color(0xFF9ca3af), fontSize: 12),
          overflow: TextOverflow.ellipsis)),
    ]),
  );
}
