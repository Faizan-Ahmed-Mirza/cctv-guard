import 'package:flutter/material.dart';
import '../services/auth_service.dart';
import '../services/alert_service.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen>
    with SingleTickerProviderStateMixin {
  final _formKey  = GlobalKey<FormState>();
  final _userCtrl = TextEditingController(text: 'admin');
  final _passCtrl = TextEditingController();
  bool  _loading  = false;
  bool  _obscure  = true;
  String? _error;

  late AnimationController _animCtrl;
  late Animation<double>   _fadeAnim;
  late Animation<Offset>   _slideAnim;

  @override
  void initState() {
    super.initState();
    _animCtrl = AnimationController(
      vsync: this, duration: const Duration(milliseconds: 600));
    _fadeAnim  = CurvedAnimation(parent: _animCtrl, curve: Curves.easeOut);
    _slideAnim = Tween<Offset>(begin: const Offset(0, 0.08), end: Offset.zero)
        .animate(CurvedAnimation(parent: _animCtrl, curve: Curves.easeOut));
    _animCtrl.forward();
  }

  @override
  void dispose() {
    _animCtrl.dispose();
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
      try { await AlertService().connect(); } catch (_) {}
      if (mounted) Navigator.pushReplacementNamed(context, '/home');
    } else {
      setState(() => _error = 'Invalid credentials or server unreachable.\nCheck Settings → Server URL.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0a0a0f),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 32),
            child: FadeTransition(
              opacity: _fadeAnim,
              child: SlideTransition(
                position: _slideAnim,
                child: Form(
                  key: _formKey,
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      // ── Logo ──────────────────────────────────────────
                      Container(
                        width: 88, height: 88,
                        decoration: BoxDecoration(
                          gradient: const LinearGradient(
                            colors: [Color(0xFF3b82f6), Color(0xFF6366f1)],
                            begin: Alignment.topLeft,
                            end: Alignment.bottomRight,
                          ),
                          borderRadius: BorderRadius.circular(24),
                          boxShadow: [
                            BoxShadow(
                              color: const Color(0xFF3b82f6).withOpacity(0.45),
                              blurRadius: 24,
                              offset: const Offset(0, 10),
                            ),
                          ],
                        ),
                        child: const Icon(Icons.videocam_rounded,
                            color: Colors.white, size: 44),
                      ),
                      const SizedBox(height: 24),
                      const Text('CCTV Guard',
                          style: TextStyle(
                              fontSize: 30, fontWeight: FontWeight.w800,
                              color: Colors.white, letterSpacing: -0.5)),
                      const SizedBox(height: 6),
                      const Text('AI Surveillance · Real-time Alerts',
                          style: TextStyle(fontSize: 13, color: Color(0xFF6b7280))),
                      const SizedBox(height: 48),

                      // ── Error banner ──────────────────────────────────
                      if (_error != null) ...[
                        Container(
                          padding: const EdgeInsets.symmetric(
                              horizontal: 14, vertical: 12),
                          decoration: BoxDecoration(
                            color: const Color(0xFF7f1d1d),
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(
                                color: const Color(0xFFef4444).withOpacity(0.4)),
                          ),
                          child: Row(children: [
                            const Icon(Icons.error_outline,
                                color: Color(0xFFef4444), size: 18),
                            const SizedBox(width: 10),
                            Expanded(child: Text(_error!,
                                style: const TextStyle(
                                    color: Color(0xFFfca5a5), fontSize: 13,
                                    height: 1.4))),
                          ]),
                        ),
                        const SizedBox(height: 20),
                      ],

                      // ── Username ──────────────────────────────────────
                      TextFormField(
                        controller: _userCtrl,
                        textInputAction: TextInputAction.next,
                        decoration: const InputDecoration(
                          labelText: 'Username',
                          prefixIcon: Icon(Icons.person_outline,
                              color: Color(0xFF6b7280)),
                        ),
                        style: const TextStyle(color: Colors.white),
                        validator: (v) =>
                            (v == null || v.isEmpty) ? 'Username is required' : null,
                      ),
                      const SizedBox(height: 14),

                      // ── Password ──────────────────────────────────────
                      TextFormField(
                        controller: _passCtrl,
                        obscureText: _obscure,
                        textInputAction: TextInputAction.done,
                        onFieldSubmitted: (_) => _login(),
                        decoration: InputDecoration(
                          labelText: 'Password',
                          prefixIcon: const Icon(Icons.lock_outline,
                              color: Color(0xFF6b7280)),
                          suffixIcon: IconButton(
                            icon: Icon(
                              _obscure
                                  ? Icons.visibility_off_outlined
                                  : Icons.visibility_outlined,
                              color: const Color(0xFF6b7280), size: 20),
                            onPressed: () =>
                                setState(() => _obscure = !_obscure),
                          ),
                        ),
                        style: const TextStyle(color: Colors.white),
                        validator: (v) =>
                            (v == null || v.isEmpty) ? 'Password is required' : null,
                      ),
                      const SizedBox(height: 28),

                      // ── Sign In button ────────────────────────────────
                      SizedBox(
                        width: double.infinity,
                        height: 50,
                        child: ElevatedButton(
                          onPressed: _loading ? null : _login,
                          style: ElevatedButton.styleFrom(
                            backgroundColor: const Color(0xFF3b82f6),
                            disabledBackgroundColor:
                                const Color(0xFF1f2937),
                            shape: RoundedRectangleBorder(
                                borderRadius: BorderRadius.circular(10)),
                          ),
                          child: _loading
                              ? const SizedBox(
                                  width: 22, height: 22,
                                  child: CircularProgressIndicator(
                                      strokeWidth: 2.5,
                                      color: Colors.white))
                              : const Text('Sign In',
                                  style: TextStyle(
                                      fontSize: 16,
                                      fontWeight: FontWeight.w700)),
                        ),
                      ),
                      const SizedBox(height: 32),

                      // ── Server URL hint ───────────────────────────────
                      GestureDetector(
                        onTap: _showServerDialog,
                        child: Container(
                          padding: const EdgeInsets.symmetric(
                              horizontal: 14, vertical: 12),
                          decoration: BoxDecoration(
                            color: const Color(0xFF111827),
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(color: const Color(0xFF1f2937)),
                          ),
                          child: Row(children: [
                            const Icon(Icons.dns_outlined,
                                color: Color(0xFF6b7280), size: 16),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  const Text('Server',
                                      style: TextStyle(
                                          color: Color(0xFF6b7280),
                                          fontSize: 11,
                                          fontWeight: FontWeight.w600)),
                                  const SizedBox(height: 2),
                                  Text(AlertService().baseUrl,
                                      style: const TextStyle(
                                          color: Color(0xFF9ca3af),
                                          fontSize: 12),
                                      overflow: TextOverflow.ellipsis),
                                ],
                              ),
                            ),
                            const Icon(Icons.edit_outlined,
                                color: Color(0xFF4b5563), size: 15),
                          ]),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  void _showServerDialog() {
    final ctrl = TextEditingController(text: AlertService().baseUrl);
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: const Color(0xFF111827),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        title: const Text('Server URL',
            style: TextStyle(color: Colors.white, fontSize: 16,
                fontWeight: FontWeight.w700)),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          TextField(
            controller: ctrl,
            style: const TextStyle(color: Colors.white, fontSize: 14),
            keyboardType: TextInputType.url,
            decoration: const InputDecoration(
              hintText: 'http://192.168.x.x:5176',
              prefixIcon: Icon(Icons.dns_outlined, color: Color(0xFF6b7280)),
            ),
          ),
          const SizedBox(height: 10),
          const Text(
            'Enter your backend server IP.\nFind it with ipconfig → Wi-Fi IPv4.',
            style: TextStyle(color: Color(0xFF6b7280), fontSize: 12, height: 1.5),
          ),
        ]),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Cancel',
                style: TextStyle(color: Color(0xFF6b7280))),
          ),
          ElevatedButton(
            onPressed: () async {
              await AlertService().saveUrl(ctrl.text.trim());
              if (mounted) {
                Navigator.pop(ctx);
                setState(() {}); // refresh the URL shown in the hint
              }
            },
            child: const Text('Save'),
          ),
        ],
      ),
    );
  }
}
