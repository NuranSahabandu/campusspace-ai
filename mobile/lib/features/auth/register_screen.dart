import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/router.dart';
import '../../core/validators.dart';
import 'auth_controller.dart';
import 'auth_form.dart';

/// Self-registration always creates a Student account.
class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  static const emailTaken = 'Email is already registered';

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _fullName = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _submitting = false;
  String? _error;
  Problem? _problem;

  @override
  void dispose() {
    _fullName.dispose();
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
      await ref
          .read(authControllerProvider.notifier)
          .register(_fullName.text.trim(), _email.text.trim(), _password.text);
    } on WebPortalOnlyException {
      error = WebPortalOnlyException.message;
    } catch (e) {
      problem = Problem.from(e);
      error = switch (problem.status) {
        409 => RegisterScreen.emailTaken,
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
      title: 'Create a student account',
      children: [
        if (_error case final error?) FormErrorBanner(error),
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              TextFormField(
                key: const Key('register.fullName'),
                controller: _fullName,
                decoration: const InputDecoration(labelText: 'Full name'),
                autofillHints: const [AutofillHints.name],
                textCapitalization: TextCapitalization.words,
                textInputAction: TextInputAction.next,
                validator: validateFullName,
                forceErrorText: _problem?.fieldError('fullName'),
              ),
              const SizedBox(height: 12),
              TextFormField(
                key: const Key('register.email'),
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
                key: const Key('register.password'),
                controller: _password,
                decoration: const InputDecoration(
                  labelText: 'Password',
                  helperText: '8 to 100 characters',
                ),
                obscureText: true,
                autofillHints: const [AutofillHints.newPassword],
                textInputAction: TextInputAction.done,
                onFieldSubmitted: (_) => _submit(),
                validator: validateNewPassword,
                forceErrorText: _problem?.fieldError('password'),
              ),
              const SizedBox(height: 24),
              SubmitButton(label: 'Create account', loading: _submitting, onPressed: _submit),
            ],
          ),
        ),
        const SizedBox(height: 12),
        TextButton(
          onPressed: _submitting ? null : () => context.go(AppRoutes.login),
          child: const Text('Already have an account? Sign in'),
        ),
      ],
    );
  }
}
