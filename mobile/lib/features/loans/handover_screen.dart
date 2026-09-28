import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/router.dart';
import '../rooms/room_widgets.dart';
import 'handovers_screen.dart';
import 'loan_widgets.dart';
import 'loans_providers.dart';
import 'loans_repository.dart';
import 'models.dart';

/// UC10 for one of today's bookings: hand over an Available item per reserved type while fewer than reserved are out,
/// and see the booking's open loans (to check in) and returned ones. The server decides every rule; its 409 title is
/// shown exactly as sent.
class HandoverScreen extends ConsumerStatefulWidget {
  const HandoverScreen({super.key, required this.bookingId});

  static const notToday = "This booking isn't in today's handovers";

  /// Null when the route's id is not a number.
  final int? bookingId;

  @override
  ConsumerState<HandoverScreen> createState() => _HandoverScreenState();
}

class _HandoverScreenState extends ConsumerState<HandoverScreen> {
  bool _busy = false;

  Future<void> _handOver(Handover handover, HandoverLine line) async {
    final item = await showModalBottomSheet<EquipmentItem>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => ItemPickerSheet(line: line),
    );
    if (item == null || !mounted) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Hand over ${item.assetTag}?'),
        content: Text('${line.typeName} for ${handover.roomCode}, '
            '${formatCampusTimeRange(handover.start, handover.end)} (${handover.requesterName}).'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Hand over')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    final container = ProviderScope.containerOf(context, listen: false);
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _busy = true);
    try {
      await container.read(loansRepositoryProvider).checkout(bookingId: handover.bookingId, itemId: item.id);
      showMessage(messenger, 'Handed over ${item.assetTag}');
    } catch (e) {
      // A 409 (too early, all out, item taken meanwhile…) is shown exactly as the server words it.
      final problem = Problem.from(e);
      showMessage(messenger, problem.fieldError('itemId') ?? problem.fieldError('bookingId') ?? problem.title);
    } finally {
      // Success or not, the counts and items may have changed.
      invalidateLoanLists(container, bookingId: handover.bookingId, typeId: line.typeId);
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final bookingId = widget.bookingId;
    Widget frame(Widget child) =>
        Scaffold(appBar: AppBar(title: const Text('Handover')), body: SafeArea(child: child));
    const notToday = MessageView(icon: Icons.event_busy_outlined, message: HandoverScreen.notToday);
    if (bookingId == null) return frame(notToday);

    final today = ref.watch(todayHandoversProvider);
    final loans = ref.watch(bookingLoansProvider(bookingId));
    Future<void> refresh() => Future.wait([
          refreshProvider(ref, todayHandoversProvider.future),
          refreshProvider(ref, bookingLoansProvider(bookingId).future),
        ]);

    return frame(switch (today) {
      AsyncValue(:final value?) => switch (value.where((h) => h.bookingId == bookingId).firstOrNull) {
          null => notToday,
          final handover => RefreshIndicator(
              onRefresh: refresh,
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.all(16),
                children: [
                  _Header(handover),
                  const SizedBox(height: 16),
                  Text('Reserved equipment', style: Theme.of(context).textTheme.titleMedium),
                  for (final line in handover.lines)
                    ListTile(
                      key: Key('line.${line.typeId}'),
                      contentPadding: EdgeInsets.zero,
                      title: Text(line.typeName),
                      subtitle: Text(handoverLineCounts(line)),
                      trailing: line.canHandOver
                          ? FilledButton.tonal(
                              key: Key('handOver.${line.typeId}'),
                              onPressed: _busy ? null : () => _handOver(handover, line),
                              child: const Text('Hand over'),
                            )
                          : const Chip(label: Text('All out'), visualDensity: VisualDensity.compact),
                    ),
                  const SizedBox(height: 16),
                  switch (loans) {
                    AsyncValue(:final value?) => _BookingLoans(value),
                    AsyncValue(:final error?) => LoadErrorView(
                        error: error,
                        onRetry: () => ref.invalidate(bookingLoansProvider(bookingId)),
                      ),
                    _ => const Center(child: CircularProgressIndicator()),
                  },
                ],
              ),
            ),
        },
      AsyncValue(:final error?) => LoadErrorView(error: error, onRetry: () => ref.invalidate(todayHandoversProvider)),
      _ => const Center(child: CircularProgressIndicator()),
    });
  }
}

class _Header extends StatelessWidget {
  const _Header(this.handover);

  final Handover handover;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('${formatCampusTimeRange(handover.start, handover.end)} · ${handover.roomCode}',
            style: textTheme.headlineSmall),
        const SizedBox(height: 4),
        Text(handover.requesterName),
        Text(formatCampusDate(handover.start), style: textTheme.bodySmall),
      ],
    );
  }
}

/// The booking's open loans (with Check in) and returned ones (condition, late flag, photo).
class _BookingLoans extends StatelessWidget {
  const _BookingLoans(this.loans);

  final List<Loan> loans;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final open = loans.where((l) => l.isOpen).toList();
    final returned = loans.where((l) => !l.isOpen).toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Out now', style: textTheme.titleMedium),
        if (open.isEmpty) const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('Nothing is out')),
        for (final loan in open)
          ListTile(
            key: Key('open.${loan.id}'),
            contentPadding: EdgeInsets.zero,
            title: Row(
              children: [
                Flexible(child: Text('${loan.assetTag} · ${loan.typeCode}')),
                if (loan.isOverdue) ...[const SizedBox(width: 8), const FlagChip('Overdue')],
              ],
            ),
            subtitle: Text('Due ${formatCampusDateTime(loan.dueAt)} · by ${loan.checkedOutByName}'),
            trailing: TextButton(
              onPressed: () => context.push(AppRoutes.checkIn(loan.id)),
              child: const Text('Check in'),
            ),
          ),
        const SizedBox(height: 16),
        Text('Returned', style: textTheme.titleMedium),
        if (returned.isEmpty)
          const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('Nothing returned yet')),
        for (final loan in returned)
          ListTile(
            key: Key('returned.${loan.id}'),
            contentPadding: EdgeInsets.zero,
            title: Row(
              children: [
                Flexible(child: Text('${loan.assetTag} · ${loan.typeCode}')),
                if (loan.isLateReturn) ...[const SizedBox(width: 8), const FlagChip('Late return')],
              ],
            ),
            subtitle: Text('${ItemConditions.label(loan.returnCondition ?? '')} · '
                'checked in ${formatCampusDateTime(loan.checkedInAt!)}'),
            trailing: loan.hasPhoto ? const Icon(Icons.photo_camera_outlined, semanticLabel: 'Photo on file') : null,
          ),
      ],
    );
  }
}

/// Lists the Available items of one reserved type; tapping one pops it.
class ItemPickerSheet extends ConsumerWidget {
  const ItemPickerSheet({super.key, required this.line});

  static const empty = 'No available items of this type';

  final HandoverLine line;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final items = ref.watch(availableItemsProvider(line.typeId));
    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.7),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              child: Text('Available ${line.typeCode}', style: Theme.of(context).textTheme.titleMedium),
            ),
            Flexible(
              child: switch (items) {
                AsyncValue(:final value?) when value.isEmpty =>
                  const MessageView(icon: Icons.inventory_2_outlined, message: empty),
                AsyncValue(:final value?) => ListView(
                    shrinkWrap: true,
                    children: [
                      for (final item in value)
                        ListTile(
                          key: Key('item.${item.id}'),
                          leading: const Icon(Icons.qr_code_2),
                          title: Text(item.assetTag),
                          subtitle: Text(ItemConditions.label(item.condition)),
                          onTap: () => Navigator.pop(context, item),
                        ),
                    ],
                  ),
                AsyncValue(:final error?) =>
                  LoadErrorView(error: error, onRetry: () => ref.invalidate(availableItemsProvider(line.typeId))),
                _ => const Padding(padding: EdgeInsets.all(24), child: Center(child: CircularProgressIndicator())),
              },
            ),
          ],
        ),
      ),
    );
  }
}
