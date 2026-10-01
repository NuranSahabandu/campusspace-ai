import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/config.dart';
import '../../core/router.dart';
import '../../core/validators.dart';
import 'auth_controller.dart';
import 'auth_form.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  static const invalidCredentials = 'Invalid email or password';

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _submitting = false;
  String? _error;
  Problem? _problem;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _error = null;
      _problem = null;
    });
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    String? error;
    Problem? problem;
    try {
      // On success the router's redirect leaves this screen.
      await ref.read(authControllerProvider.notifier).login(_email.text.trim(), _password.text);
    } on WebPortalOnlyException {
      error = WebPortalOnlyException.message;
    } catch (e) {
      problem = Problem.from(e);
      error = switch (problem.status) {
        401 => LoginScreen.invalidCredentials,
        400 when problem.fieldErrors.isNotEmpty => null,
        _ => problem.title,
      };
    }
    if (!mounted) return;
    setState(() {
      _submitting = false;
      _error = error;
      _problem = problem;
    });
  }

  @override
  Widget build(BuildContext context) {
    return AuthFormScaffold(
      title: 'Sign in to CampusSpace',
      children: [
        if (_error case final error?) FormErrorBanner(error),
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              TextFormField(
                key: const Key('login.email'),
                controller: _email,
                decoration: const InputDecoration(labelText: 'Email'),
                keyboardType: TextInputType.emailAddress,
                autofillHints: const [AutofillHints.email],
                textInputAction: TextInputAction.next,
                validator: validateEmail,
                forceErrorText: _problem?.fieldError('email'),
              ),
              const SizedBox(height: 12),
              TextFormField(
                key: const Key('login.password'),
                controller: _password,
                decoration: const InputDecoration(labelText: 'Password'),
                obscureText: true,
                autofillHints: const [AutofillHints.password],
                textInputAction: TextInputAction.done,
                onFieldSubmitted: (_) => _submit(),
                validator: validateLoginPassword,
                forceErrorText: _problem?.fieldError('password'),
              ),
              const SizedBox(height: 24),
              SubmitButton(label: 'Sign in', loading: _submitting, onPressed: _submit),
            ],
          ),
        ),
        const SizedBox(height: 12),
        TextButton(
          onPressed: _submitting ? null : () => context.go(AppRoutes.register),
          child: const Text('New student? Create an account'),
        ),
        if (appVersionLabel() case final version when version.isNotEmpty) ...[
          const SizedBox(height: 24),
          Text(
            version,
            key: const Key('login.version'),
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ],
    );
  }
}
