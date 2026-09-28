import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/campus_time.dart';
import '../../core/router.dart';
import '../rooms/room_widgets.dart';
import 'loan_widgets.dart';
import 'loans_providers.dart';

/// UC12: open loans past their due time, longest overdue first. Each opens the check-in screen.
class OverdueScreen extends ConsumerWidget {
  const OverdueScreen({super.key});

  static const title = 'Overdue equipment';
  static const empty = 'No overdue loans';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final overdue = ref.watch(overdueLoansProvider);
    return Scaffold(
      appBar: AppBar(title: const Text(title)),
      body: SafeArea(
        child: switch (overdue) {
          AsyncValue(:final value?) => RefreshIndicator(
              onRefresh: () => refreshProvider(ref, overdueLoansProvider.future),
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.symmetric(vertical: 8),
                children: [
                  if (value.items.isEmpty)
                    const Padding(
                      padding: EdgeInsets.only(top: 48),
                      child: MessageView(icon: Icons.task_alt, message: empty),
                    ),
                  for (final loan in value.items)
                    ListTile(
                      key: Key('overdue.${loan.id}'),
                      isThreeLine: true,
                      leading: const Icon(Icons.assignment_late_outlined),
                      title: Text('${loan.assetTag} · ${loan.typeCode}'),
                      subtitle: Text('Room ${loan.roomCode} · due ${formatCampusDateTime(loan.dueAt)}\n'
                          'Checked out by ${loan.checkedOutByName}'),
                      trailing: TextButton(
                        onPressed: () => context.push(AppRoutes.checkIn(loan.id)),
                        child: const Text('Check in'),
                      ),
                    ),
                  if (value.total > value.items.length)
                    Padding(
                      padding: const EdgeInsets.all(16),
                      child: Text('Showing ${value.items.length} of ${value.total}', textAlign: TextAlign.center),
                    ),
                ],
              ),
            ),
          AsyncValue(:final error?) => LoadErrorView(error: error, onRetry: () => ref.invalidate(overdueLoansProvider)),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }
}
