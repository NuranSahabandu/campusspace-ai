import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/campus_time.dart';
import '../../core/router.dart';
import '../rooms/room_widgets.dart';
import 'loan_widgets.dart';
import 'loans_providers.dart';
import 'models.dart';

/// UC09, the technician's home: today's (campus date) bookings that have equipment reserved, by start.
class TodayHandoversView extends ConsumerWidget {
  const TodayHandoversView({super.key});

  static const title = "Today's handovers";
  static const empty = 'No handovers today';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final today = ref.watch(todayHandoversProvider);
    final textTheme = Theme.of(context).textTheme;
    return switch (today) {
      AsyncValue(:final value?) => RefreshIndicator(
          onRefresh: () => refreshProvider(ref, todayHandoversProvider.future),
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            children: [
              Text(title, style: textTheme.titleLarge),
              const SizedBox(height: 12),
              if (value.isEmpty)
                const Padding(
                  padding: EdgeInsets.only(top: 48),
                  child: MessageView(icon: Icons.inventory_2_outlined, message: empty),
                ),
              for (final handover in value) HandoverCard(handover),
            ],
          ),
        ),
      AsyncValue(:final error?) => LoadErrorView(error: error, onRetry: () => ref.invalidate(todayHandoversProvider)),
      _ => const Center(child: CircularProgressIndicator()),
    };
  }
}

/// "MIC-WIRELESS: 1 / 2 out · 2 returned".
String handoverLineCounts(HandoverLine line) =>
    '${line.typeCode}: ${line.out} / ${line.reserved} out · ${line.returned} returned';

/// One booking: campus time range, room, requester and each reserved type's counts. Opens the handover screen.
class HandoverCard extends StatelessWidget {
  const HandoverCard(this.handover, {super.key});

  final Handover handover;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Card(
      key: Key('handover.${handover.bookingId}'),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push(AppRoutes.handover(handover.bookingId)),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      '${formatCampusTimeRange(handover.start, handover.end)} · ${handover.roomCode}',
                      style: textTheme.titleMedium,
                    ),
                  ),
                  const Icon(Icons.chevron_right),
                ],
              ),
              Text(handover.requesterName, style: textTheme.bodyMedium),
              const SizedBox(height: 8),
              for (final line in handover.lines)
                Padding(
                  padding: const EdgeInsets.only(top: 2),
                  child: Row(
                    children: [
                      Icon(line.canHandOver ? Icons.radio_button_unchecked : Icons.check_circle_outline, size: 16),
                      const SizedBox(width: 6),
                      Expanded(child: Text(handoverLineCounts(line))),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
