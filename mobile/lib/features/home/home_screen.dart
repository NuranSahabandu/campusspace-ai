import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../auth/models.dart';

/// Phase 0 placeholder home: what each mobile role will see first (plan §13).
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // The router only shows this screen when signed in.
    final user = switch (ref.watch(authControllerProvider).value) {
      Authenticated(:final user) => user,
      _ => null,
    };
    if (user == null) return const Scaffold();

    final textTheme = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(user.fullName),
            Text(Roles.label(user.role), style: textTheme.bodySmall),
          ],
        ),
        actions: [
          IconButton(
            tooltip: 'Log out',
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: SafeArea(
        child: user.role == Roles.labTechnician ? const _TechnicianHome() : const _RequesterHome(),
      ),
    );
  }
}

class _RequesterHome extends StatelessWidget {
  const _RequesterHome();

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Text('My requests', style: textTheme.titleLarge),
        const SizedBox(height: 32),
        const Icon(Icons.event_note_outlined, size: 48),
        const SizedBox(height: 12),
        Text('No requests yet', textAlign: TextAlign.center, style: textTheme.titleMedium),
        const SizedBox(height: 24),
        const FilledButton(onPressed: null, child: Text('New request')),
        const SizedBox(height: 8),
        Text('Coming in Phase 1 (Component C)', textAlign: TextAlign.center, style: textTheme.bodySmall),
      ],
    );
  }
}

class _TechnicianHome extends StatelessWidget {
  const _TechnicianHome();

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Text("Today's handovers", style: textTheme.titleLarge),
        const SizedBox(height: 32),
        const Icon(Icons.inventory_2_outlined, size: 48),
        const SizedBox(height: 12),
        Text('Coming in Phase 1 (Component B)', textAlign: TextAlign.center, style: textTheme.titleMedium),
      ],
    );
  }
}
