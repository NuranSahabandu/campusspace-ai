import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/router.dart';

import '../auth/auth_controller.dart';
import '../auth/models.dart';
import '../loans/handovers_screen.dart';

/// Home: what each mobile role sees first (plan §13). Requesters make and follow requests and browse rooms;
/// lab technicians see today's handovers, with the overdue list one tap away.
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
    final isTechnician = user.role == Roles.labTechnician;
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
          if (isTechnician)
            IconButton(
              tooltip: 'Overdue',
              icon: const Icon(Icons.assignment_late_outlined),
              onPressed: () => context.push(AppRoutes.overdue),
            ),
          IconButton(
            tooltip: 'Log out',
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: SafeArea(
        child: isTechnician ? const TodayHandoversView() : const _RequesterHome(),
      ),
    );
  }
}

class _RequesterHome extends StatelessWidget {
  const _RequesterHome();

  @override
  Widget build(BuildContext context) {
    Widget entry(IconData icon, String title, String subtitle, String route) => Card(
          clipBehavior: Clip.antiAlias,
          child: ListTile(
            leading: Icon(icon),
            title: Text(title),
            subtitle: Text(subtitle),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => context.push(route),
          ),
        );

    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        entry(Icons.add_circle_outline, 'New request', 'Ask for a room and equipment', AppRoutes.newRequest),
        const SizedBox(height: 12),
        entry(Icons.event_note_outlined, 'My requests', 'Follow your requests and their status', AppRoutes.requests),
        const SizedBox(height: 12),
        entry(Icons.meeting_room_outlined, 'Browse rooms', 'Search by building, type, capacity and features',
            AppRoutes.rooms),
      ],
    );
  }
}
