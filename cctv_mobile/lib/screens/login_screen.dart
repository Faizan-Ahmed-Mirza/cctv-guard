import 'package:flutter/material.dart';
import '../services/auth_service.dart';
import '../services/alert_service.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _formKey    = GlobalKey<FormState>();
  final _userCtrl   = TextEditingController(text: 'admin');
  final _passCtrl   = TextEditingController();
  bool  _loading    = false;
  bool  _obscure    = true;
  String? _error;

  @override
  void dispose() {
    _userCtrl.dispose();
    _passCtrl.dispose();
    super.dispose();
  }

  Future<void> _login() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() { _loading = true; _error = null; });

    final ok = await AuthService().login(_userCtrl.text.trim(), _passCtrl.text);

    if (!mounted) return;
    setState(() => _loading = false);

    if (ok) {
      // Connect to SignalR hub after successful login
      try { await AlertService().connect(); } catch (_) {}
      if (mounted) Navigator.pushReplacementNamed(context, '/dashboard');
    } else {
      setState(() => _error = 'Invalid credentials or server unreachable.\nCheck the IP in alert_service.dart.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(28),
            child: Form(
              key: _formKey,
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  // Logo / Brand
                  Container(
                    width: 72, height: 72,
                    decoration: BoxDecoration(
                      color: const Color(0xFF3b82f6),
                      borderRadius: BorderRadius.circular(18),
                    ),
                    child: const Icon(Icons.videocam, color: Colors.white, size: 38),
                  ),
                  const SizedBox(height: 20),
                  const Text(
                    'CCTV Guard',
                    style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: Colors.white),
                  ),
                  const SizedBox(height: 6),
                  const Text(
                    'AI Surveillance Companion',
                    style: TextStyle(fontSize: 14, color: Color(0xFF9ca3af)),
                  ),
                  const SizedBox(height: 40),

                  // Error banner
                  if (_error != null) ...[
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: const Color(0xFF7f1d1d),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: const Color(0xFFef4444).withOpacity(0.4)),
                      ),
                      child: Row(
                        children: [
                          const Icon(Icons.error_outline, color: Color(0xFFef4444), size: 18),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Text(_error!,
                              style: const TextStyle(color: Color(0xFFfca5a5), fontSize: 13)),
                          ),
                        ],
                      ),
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
                    validator: (v) => v == null || v.isEmpty ? 'Enter username' : null,
                  ),
                  const SizedBox(height: 16),

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
                    validator: (v) => v == null || v.isEmpty ? 'Enter password' : null,
                    onFieldSubmitted: (_) => _login(),
                  ),
                  const SizedBox(height: 28),

                  // Login button
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _loading ? null : _login,
                      child: _loading
                          ? const SizedBox(
                              height: 20, width: 20,
                              child: CircularProgressIndicator(
                                strokeWidth: 2, color: Colors.white))
                          : const Text('Sign In'),
                    ),
                  ),
                  const SizedBox(height: 24),

                  // Server info hint
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color: const Color(0xFF1f2937),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: const Row(
                      children: [
                        Icon(Icons.info_outline, color: Color(0xFF6b7280), size: 16),
                        SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            'Default: admin / Admin@1234\nChange server IP in alert_service.dart',
                            style: TextStyle(color: Color(0xFF6b7280), fontSize: 12),
                          ),
                        ),
                      ],
                    ),
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
