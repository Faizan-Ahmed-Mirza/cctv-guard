import 'package:flutter/material.dart';
import '../services/auth_service.dart';
import '../services/alert_service.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _formKey  = GlobalKey<FormState>();
  final _userCtrl = TextEditingController(text: 'admin');
  final _passCtrl = TextEditingController();
  final _ipCtrl   = TextEditingController();
  bool  _loading  = false;
  bool  _obscure  = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadIp();
  }

  Future<void> _loadIp() async {
    await AlertService().loadSavedUrl();
    _ipCtrl.text = AlertService().baseUrl;
  }

  Future<void> _showIpDialog() async {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: const Color(0xFF111827),
        title: const Text('Server Settings', style: TextStyle(color: Colors.white)),
        content: TextField(
          controller: _ipCtrl,
          style: const TextStyle(color: Colors.white),
          decoration: const InputDecoration(
            labelText: 'Server Base URL',
            hintText: 'http://192.168.x.x:5176',
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Cancel')),
          TextButton(
            onPressed: () async {
              await AlertService().saveUrl(_ipCtrl.text);
              if (mounted) Navigator.pop(ctx);
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(content: Text('Server URL updated')),
              );
            },
            child: const Text('Save'),
          ),
        ],
      ),
    );
  }

  Future<void> _login() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() { _loading = true; _error = null; });

    final ok = await AuthService().login(_userCtrl.text.trim(), _passCtrl.text);
    if (!mounted) return;
    setState(() => _loading = false);

    if (ok) {
      try { await AlertService().connect(); } catch (_) {}
      if (mounted) Navigator.pushReplacementNamed(context, '/home');
    } else {
      setState(() => _error =
          'Login failed. Check credentials or server URL in Settings.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        actions: [
          IconButton(
            icon: const Icon(Icons.settings_outlined, color: Color(0xFF6b7280)),
            onPressed: _showIpDialog,
            tooltip: 'Server Settings',
          ),
        ],
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 40),
            child: Form(
              key: _formKey,
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  // Brand
                  Container(
                    width: 80, height: 80,
                    decoration: BoxDecoration(
                      gradient: const LinearGradient(
                        colors: [Color(0xFF3b82f6), Color(0xFF6366f1)],
                        begin: Alignment.topLeft, end: Alignment.bottomRight,
                      ),
                      borderRadius: BorderRadius.circular(22),
                      boxShadow: [
                        BoxShadow(color: const Color(0xFF3b82f6).withOpacity(0.4),
                            blurRadius: 20, offset: const Offset(0, 8)),
                      ],
                    ),
                    child: const Icon(Icons.videocam_rounded, color: Colors.white, size: 42),
                  ),
                  const SizedBox(height: 22),
                  const Text('CCTV Guard',
                      style: TextStyle(fontSize: 28, fontWeight: FontWeight.w800,
                          color: Colors.white, letterSpacing: -0.5)),
                  const SizedBox(height: 6),
                  const Text('AI Surveillance · Real-time Alerts',
                      style: TextStyle(fontSize: 13, color: Color(0xFF6b7280))),
                  const SizedBox(height: 44),

                  // Error
                  if (_error != null) ...[
                    Container(
                      padding: const EdgeInsets.all(13),
                      decoration: BoxDecoration(
                        color: const Color(0xFF7f1d1d),
                        borderRadius: BorderRadius.circular(10),
                        border: Border.all(color: const Color(0xFFef4444).withOpacity(0.4)),
                      ),
                      child: Row(children: [
                        const Icon(Icons.error_outline, color: Color(0xFFef4444), size: 18),
                        const SizedBox(width: 10),
                        Expanded(child: Text(_error!,
                            style: const TextStyle(color: Color(0xFFfca5a5), fontSize: 13))),
                      ]),
                    ),
                    const SizedBox(height: 20),
                  ],

                  // Username
                  TextFormField(
                    controller: _userCtrl,
                    decoration: const InputDecoration(
                      labelText: 'Username',
                      prefixIcon: Icon(Icons.person_outline, color: Color(0xFF6b7280)),
                    ),
                    style: const TextStyle(color: Colors.white),
                    validator: (v) => (v == null || v.isEmpty) ? 'Required' : null,
                  ),
                  const SizedBox(height: 14),

                  // Password
                  TextFormField(
                    controller: _passCtrl,
                    obscureText: _obscure,
                    decoration: InputDecoration(
                      labelText: 'Password',
                      prefixIcon: const Icon(Icons.lock_outline, color: Color(0xFF6b7280)),
                      suffixIcon: IconButton(
                        icon: Icon(_obscure ? Icons.visibility_off : Icons.visibility,
                            color: const Color(0xFF6b7280)),
                        onPressed: () => setState(() => _obscure = !_obscure),
                      ),
                    ),
                    style: const TextStyle(color: Colors.white),
                    validator: (v) => (v == null || v.isEmpty) ? 'Required' : null,
                    onFieldSubmitted: (_) => _login(),
                  ),
                  const SizedBox(height: 14),

                  // Server IP (Visible for debugging)
                  TextFormField(
                    controller: _ipCtrl,
                    decoration: const InputDecoration(
                      labelText: 'Server URL',
                      prefixIcon: Icon(Icons.dns_outlined, color: Color(0xFF6b7280)),
                      hintText: 'http://192.168.x.x:5176',
                    ),
                    style: const TextStyle(color: Colors.white, fontSize: 13),
                    onChanged: (v) => AlertService().saveUrl(v),
                  ),
                  const SizedBox(height: 28),

                  // Sign in button
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _loading ? null : _login,
                      child: _loading
                          ? const SizedBox(width: 20, height: 20,
                              child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                          : const Text('Sign In'),
                    ),
                  ),
                  const SizedBox(height: 28),

                  // Hint
                  Container(
                    padding: const EdgeInsets.all(13),
                    decoration: BoxDecoration(
                      color: const Color(0xFF1f2937),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: const Row(children: [
                      Icon(Icons.info_outline, color: Color(0xFF6b7280), size: 15),
                      SizedBox(width: 8),
                      Expanded(child: Text(
                        'Default: admin / Admin@1234\nSet server IP in Settings after login.',
                        style: TextStyle(color: Color(0xFF6b7280), fontSize: 12),
                      )),
                    ]),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
