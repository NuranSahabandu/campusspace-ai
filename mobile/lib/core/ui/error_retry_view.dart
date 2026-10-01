import 'package:flutter/material.dart';

import '../api/problem.dart';
import 'message_view.dart';

/// The full-screen state for a failed load (every new data screen uses it):
/// - API unreachable: a clear offline message with Retry, never exception text;
/// - 403: "You don't have access to this." with no Retry (retrying cannot help);
/// - anything else: the server's Problem title with Retry.
/// A 401 never gets here for long: the dio interceptor signs out and the router shows login.
class ErrorRetryView extends StatelessWidget {
  const ErrorRetryView({super.key, required this.error, required this.onRetry});

  static const offlineTitle = 'Cannot reach the server';
  static const offlineHint = 'Check your connection and try again.';
  static const forbidden = "You don't have access to this.";

  final Object error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final problem = Problem.from(error);
    final retry = FilledButton.tonal(onPressed: onRetry, child: const Text('Retry'));
    if (problem.offline) {
      return MessageView(
        icon: Icons.cloud_off,
        message: offlineTitle,
        action: Column(
          children: [
            Text(offlineHint, textAlign: TextAlign.center, style: Theme.of(context).textTheme.bodyMedium),
            const SizedBox(height: 16),
            retry,
          ],
        ),
      );
    }
    if (problem.status == 403) return const MessageView(icon: Icons.lock_outline, message: forbidden);
    return MessageView(icon: Icons.error_outline, message: problem.title, action: retry);
  }
}

/// The inline state for a failed section load (a picker's options, a policy value): one line with the message and a
/// Retry text button. [message] defaults to the error's Problem title ("Cannot reach the server" when offline).
class InlineLoadError extends StatelessWidget {
  const InlineLoadError({super.key, required this.error, required this.onRetry, this.message});

  final Object error;
  final VoidCallback onRetry;
  final String? message;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      const Icon(Icons.error_outline, size: 20),
      const SizedBox(width: 8),
      Expanded(child: Text(message ?? Problem.from(error).title)),
      TextButton(onPressed: onRetry, child: const Text('Retry')),
    ],
  );
}
