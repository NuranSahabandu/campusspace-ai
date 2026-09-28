import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import 'models.dart';
import 'rooms_providers.dart';

/// A room's day schedule (UC03): a campus date with previous/next and a picker, the day's opening hours, and its busy
/// and free intervals in campus time. Bookings only ever show "Booked".
class RoomScheduleSection extends ConsumerWidget {
  const RoomScheduleSection({super.key, required this.roomId});

  static const title = 'Day schedule';
  static const closed = 'Closed';
  static const fullyBooked = 'Fully booked';
  static const booked = 'Booked';
  static const maintenance = 'Maintenance';
  static const free = 'Free';

  /// How far ahead the picker goes. Past days are not offered: a requester can't book them.
  static const daysAhead = 365;

  final int roomId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final date = ref.watch(scheduleDateProvider(roomId));
    final today = campusToday(ref.watch(clockProvider)());
    final last = today.add(const Duration(days: daysAhead));
    final key = (roomId: roomId, date: date);
    final schedule = ref.watch(roomScheduleProvider(key));
    final dates = ref.read(scheduleDateProvider(roomId).notifier);

    Future<void> pick() async {
      final picked = await showDatePicker(context: context, initialDate: date, firstDate: today, lastDate: last);
      if (picked != null) dates.select(picked);
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(title, style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
        Row(
          children: [
            IconButton(
              key: const Key('schedule.previous'),
              tooltip: 'Previous day',
              onPressed: date.isAfter(today) ? () => dates.move(-1) : null,
              icon: const Icon(Icons.chevron_left),
            ),
            Expanded(
              child: TextButton.icon(
                key: const Key('schedule.pick'),
                onPressed: pick,
                icon: const Icon(Icons.calendar_today_outlined, size: 18),
                label: Text(formatCampusDay(date)),
              ),
            ),
            IconButton(
              key: const Key('schedule.next'),
              tooltip: 'Next day',
              onPressed: date.isBefore(last) ? () => dates.move(1) : null,
              icon: const Icon(Icons.chevron_right),
            ),
          ],
        ),
        const SizedBox(height: 8),
        switch (schedule) {
          AsyncValue(:final value?) => _Day(value),
          AsyncValue(:final error?, isLoading: false) => Row(
              children: [
                const Icon(Icons.error_outline),
                const SizedBox(width: 8),
                Expanded(child: Text(Problem.from(error).title)),
                TextButton(onPressed: () => ref.invalidate(roomScheduleProvider(key)), child: const Text('Retry')),
              ],
            ),
          _ => const Padding(
              padding: EdgeInsets.all(16),
              child: Center(child: CircularProgressIndicator()),
            ),
        },
      ],
    );
  }
}

class _Day extends StatelessWidget {
  const _Day(this.schedule);

  final RoomSchedule schedule;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final (open, close) = (schedule.open, schedule.close);
    final entries = schedule.entries;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Icon(schedule.isClosed ? Icons.door_front_door_outlined : Icons.access_time, size: 18),
            const SizedBox(width: 8),
            Text(
              open != null && close != null
                  ? 'Open ${formatTimeOfDay(open)}–${formatTimeOfDay(close)}'
                  : RoomScheduleSection.closed,
              key: const Key('schedule.hours'),
              style: textTheme.bodyLarge,
            ),
          ],
        ),
        if (schedule.isFullyBooked)
          Card(
            key: const Key('schedule.fullyBooked'),
            color: Theme.of(context).colorScheme.errorContainer,
            child: ListTile(
              leading: const Icon(Icons.block),
              title: const Text(RoomScheduleSection.fullyBooked),
              textColor: Theme.of(context).colorScheme.onErrorContainer,
              iconColor: Theme.of(context).colorScheme.onErrorContainer,
            ),
          ),
        const SizedBox(height: 8),
        for (final (index, entry) in entries.indexed) _EntryRow(entry, key: Key('schedule.entry.$index')),
      ],
    );
  }
}

/// One interval: its campus time range, then "Booked", the blackout's reason, or "Free", each in its own colour.
class _EntryRow extends StatelessWidget {
  const _EntryRow(this.entry, {super.key});

  final ScheduleEntry entry;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;
    final dark = Theme.of(context).brightness == Brightness.dark;
    final (Color background, Color foreground, IconData icon, String label, String? caption) = switch (entry) {
      BusyInterval(isBlackout: true, :final label) => (
          scheme.tertiaryContainer,
          scheme.onTertiaryContainer,
          Icons.build_outlined,
          label,
          RoomScheduleSection.maintenance,
        ),
      BusyInterval() => (
          scheme.errorContainer,
          scheme.onErrorContainer,
          Icons.event_busy,
          RoomScheduleSection.booked,
          null,
        ),
      FreeInterval() => (
          dark ? Colors.green.shade900.withValues(alpha: 0.6) : Colors.green.shade50,
          dark ? Colors.green.shade100 : Colors.green.shade900,
          Icons.check_circle_outline,
          RoomScheduleSection.free,
          null,
        ),
    };
    return Container(
      margin: const EdgeInsets.only(bottom: 6),
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(8)),
      child: Row(
        children: [
          SizedBox(
            width: 104,
            child: Text(
              formatCampusTimeRange(entry.start, entry.end),
              style: textTheme.bodyMedium?.copyWith(color: foreground, fontFeatures: const [FontFeature.tabularFigures()]),
            ),
          ),
          Icon(icon, size: 18, color: foreground),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // A blackout reason is officer-entered text: shown as plain text only.
                Text(label, style: textTheme.bodyMedium?.copyWith(color: foreground, fontWeight: FontWeight.w600)),
                if (caption != null) Text(caption, style: textTheme.bodySmall?.copyWith(color: foreground)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
