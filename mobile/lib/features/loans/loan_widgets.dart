import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Refreshable;

import '../../core/api/problem.dart';
import '../rooms/room_widgets.dart';

/// A small error-coloured flag, such as "Overdue" or "Late return".
class FlagChip extends StatelessWidget {
  const FlagChip(this.label, {super.key, this.icon = Icons.schedule});

  final String label;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Chip(
      avatar: Icon(icon, size: 16, color: scheme.onErrorContainer),
      label: Text(label, style: TextStyle(color: scheme.onErrorContainer)),
      backgroundColor: scheme.errorContainer,
      side: BorderSide.none,
      visualDensity: VisualDensity.compact,
      materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
    );
  }
}

/// The server's message for a failed load, with a Retry button.
class LoadErrorView extends StatelessWidget {
  const LoadErrorView({super.key, required this.error, required this.onRetry});

  final Object error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => MessageView(
        icon: Icons.error_outline,
        message: Problem.from(error).title,
        action: FilledButton.tonal(onPressed: onRetry, child: const Text('Retry')),
      );
}

/// Pull-to-refresh for a FutureProvider: waits for the reload, and leaves any error to the screen's error state.
Future<void> refreshProvider<T>(WidgetRef ref, Refreshable<Future<T>> provider) =>
    ref.refresh(provider).then((_) {}, onError: (_) {});

/// Shows [text] in a SnackBar, replacing the current one.
void showMessage(ScaffoldMessengerState messenger, String text) => messenger
  ..hideCurrentSnackBar()
  ..showSnackBar(SnackBar(content: Text(text)));
