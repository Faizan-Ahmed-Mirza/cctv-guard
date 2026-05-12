import 'package:flutter/material.dart';
import '../services/alert_service.dart';
import '../services/auth_service.dart';

class SettingsTab extends StatefulWidget {
  const SettingsTab({super.key});
  @override
  State<SettingsTab> createState() => _SettingsTabState();
}

class _SettingsTabState extends State<SettingsTab> {
  final _urlCtrl = TextEditingController();
  bool _saving   = false;
  bool _saved    = false;

  @override
  void initState() {
    super.initState();
    _urlCtrl.text = AlertService().baseUrl;
  }

  @override
  void dispose() {
    _urlCtrl.dispose();
    super.dispose();
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
        const SnackBar(
          content: Text('Server URL saved. Reconnecting...'),
          backgroundColor: Color(0xFF22c55e),
          duration: Duration(seconds: 2),
        ),
      );
      Future.delayed(const Duration(seconds: 3), () {
        if (mounted) setState(() => _saved = false);
      });
    }
  }

  Future<void> _logout() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        backgroundColor: const Color(0xFF111827),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        title: const Text('Logout',
            style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700)),
        content: const Text('Are you sure you want to logout?',
            style: TextStyle(color: Color(0xFF9ca3af))),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel',
                style: TextStyle(color: Color(0xFF6b7280))),
          ),
          TextButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Logout',
                style: TextStyle(color: Color(0xFFef4444),
                    fontWeight: FontWeight.w700)),
          ),
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
    final username = AuthService().cachedUsername ?? 'User';
    final initial  = username.isNotEmpty ? username[0].toUpperCase() : 'U';

    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(16, 20, 16, 32),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [

        // ── Profile ───────────────────────────────────────────────────────
        _card(child: Row(children: [
          Container(
            width: 52, height: 52,
            decoration: BoxDecoration(
              gradient: const LinearGradient(
                colors: [Color(0xFF3b82f6), Color(0xFF6366f1)],
                begin: Alignment.topLeft, end: Alignment.bottomRight,
              ),
              borderRadius: BorderRadius.circular(15),
            ),
            child: Center(child: Text(initial,
                style: const TextStyle(color: Colors.white, fontSize: 22,
                    fontWeight: FontWeight.w800))),
          ),
          const SizedBox(width: 14),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start,
              children: [
            Text(username,
                style: const TextStyle(color: Colors.white, fontSize: 16,
                    fontWeight: FontWeight.w700)),
            const SizedBox(height: 3),
            const Text('CCTV Guard Operator',
                style: TextStyle(color: Color(0xFF6b7280), fontSize: 12)),
          ])),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
            decoration: BoxDecoration(
              color: const Color(0xFF22c55e).withOpacity(0.12),
              borderRadius: BorderRadius.circular(20),
              border: Border.all(
                  color: const Color(0xFF22c55e).withOpacity(0.35)),
            ),
            child: const Text('Active',
                style: TextStyle(color: Color(0xFF22c55e), fontSize: 11,
                    fontWeight: FontWeight.w600)),
          ),
        ])),

        const SizedBox(height: 24),

        // ── Server Configuration ──────────────────────────────────────────
        _label('Server Configuration'),
        const SizedBox(height: 10),
        _card(child: Column(crossAxisAlignment: CrossAxisAlignment.start,
            children: [
          const Text('Backend Server URL',
              style: TextStyle(color: Color(0xFF9ca3af), fontSize: 12,
                  fontWeight: FontWeight.w600)),
          const SizedBox(height: 10),
          TextField(
            controller: _urlCtrl,
            style: const TextStyle(color: Colors.white, fontSize: 14),
            keyboardType: TextInputType.url,
            decoration: const InputDecoration(
              hintText: 'http://192.168.x.x:5176',
              prefixIcon: Icon(Icons.dns_outlined,
                  color: Color(0xFF6b7280), size: 18),
            ),
          ),
          const SizedBox(height: 8),
          Row(children: [
            const Icon(Icons.info_outline,
                color: Color(0xFF4b5563), size: 13),
            const SizedBox(width: 6),
            const Expanded(child: Text(
              'Use your Wi-Fi IP address (ipconfig → IPv4 under Wi-Fi)',
              style: TextStyle(color: Color(0xFF4b5563), fontSize: 11),
            )),
          ]),
          const SizedBox(height: 14),
          SizedBox(
            width: double.infinity,
            height: 44,
            child: ElevatedButton.icon(
              onPressed: _saving ? null : _saveAndReconnect,
              icon: _saving
                  ? const SizedBox(width: 16, height: 16,
                      child: CircularProgressIndicator(
                          strokeWidth: 2, color: Colors.white))
                  : Icon(_saved ? Icons.check_rounded : Icons.save_outlined,
                      size: 17),
              label: Text(_saving
                  ? 'Saving...'
                  : _saved
                      ? 'Saved!'
                      : 'Save & Reconnect'),
              style: ElevatedButton.styleFrom(
                backgroundColor: _saved
                    ? const Color(0xFF22c55e)
                    : const Color(0xFF3b82f6),
                shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(9)),
              ),
            ),
          ),
        ])),

        const SizedBox(height: 24),

        // ── Connection Status ─────────────────────────────────────────────
        _label('Connection'),
        const SizedBox(height: 10),
        _card(child: Column(children: [
          _statusRow(
            icon: Icons.hub_outlined,
            label: 'SignalR Hub',
            value: '${AlertService().baseUrl}/hubs/alerts',
            valueColor: const Color(0xFF9ca3af),
          ),
          _divider(),
          _statusRow(
            icon: Icons.wifi_outlined,
            label: 'Protocol',
            value: 'WebSocket',
          ),
          _divider(),
          _statusRow(
            icon: Icons.lock_outline,
            label: 'Auth',
            value: 'JWT Bearer',
          ),
          _divider(),
          _statusRow(
            icon: Icons.refresh_outlined,
            label: 'Auto-reconnect',
            value: '0s → 2s → 5s → 10s → 30s',
          ),
        ])),

        const SizedBox(height: 24),

        // ── Notifications ─────────────────────────────────────────────────
        _label('Notifications'),
        const SizedBox(height: 10),
        _card(child: Column(children: [
          _statusRow(
            icon: Icons.notifications_active_outlined,
            label: 'Standard Alerts',
            value: 'Alerts tab only',
            valueColor: const Color(0xFF3b82f6),
          ),
          _divider(),
          _statusRow(
            icon: Icons.warning_amber_outlined,
            label: 'Emergency Escalations',
            value: 'Push notification + Notifications tab',
            valueColor: const Color(0xFFef4444),
          ),
        ])),

        const SizedBox(height: 24),

        // ── About ─────────────────────────────────────────────────────────
        _label('About'),
        const SizedBox(height: 10),
        _card(child: Column(children: [
          _statusRow(
            icon: Icons.phone_android_outlined,
            label: 'App',
            value: 'CCTV Guard Mobile v1.0.0',
          ),
          _divider(),
          _statusRow(
            icon: Icons.cloud_outlined,
            label: 'Backend',
            value: '.NET 8 + SignalR',
          ),
          _divider(),
          _statusRow(
            icon: Icons.psychology_outlined,
            label: 'AI Engine',
            value: 'Python FastAPI + YOLOv8',
          ),
          _divider(),
          _statusRow(
            icon: Icons.security_outlined,
            label: 'Detection',
            value: 'Weapon · Fire · Face · Intrusion',
          ),
        ])),

        const SizedBox(height: 32),

        // ── Logout ────────────────────────────────────────────────────────
        SizedBox(
          width: double.infinity,
          height: 50,
          child: OutlinedButton.icon(
            onPressed: _logout,
            icon: const Icon(Icons.logout_rounded,
                size: 18, color: Color(0xFFef4444)),
            label: const Text('Logout',
                style: TextStyle(color: Color(0xFFef4444),
                    fontWeight: FontWeight.w700, fontSize: 15)),
            style: OutlinedButton.styleFrom(
              side: const BorderSide(color: Color(0xFFef4444)),
              shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10)),
            ),
          ),
        ),
      ]),
    );
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  Widget _card({required Widget child}) => Container(
    padding: const EdgeInsets.all(16),
    decoration: BoxDecoration(
      color: const Color(0xFF111827),
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: const Color(0xFF1f2937)),
    ),
    child: child,
  );

  Widget _label(String text) => Text(text,
      style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700,
          color: Color(0xFF6b7280), letterSpacing: 0.8));

  Widget _divider() => const Divider(
      color: Color(0xFF1f2937), height: 20, thickness: 1);

  Widget _statusRow({
    required IconData icon,
    required String label,
    required String value,
    Color valueColor = const Color(0xFF9ca3af),
  }) =>
      Row(children: [
        Icon(icon, size: 16, color: const Color(0xFF4b5563)),
        const SizedBox(width: 10),
        SizedBox(
          width: 120,
          child: Text(label,
              style: const TextStyle(color: Color(0xFF6b7280), fontSize: 13)),
        ),
        Expanded(
          child: Text(value,
              style: TextStyle(color: valueColor, fontSize: 12),
              overflow: TextOverflow.ellipsis),
        ),
      ]);
}
