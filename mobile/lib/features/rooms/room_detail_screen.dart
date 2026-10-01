import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/problem.dart';
import '../../core/ui/error_retry_view.dart';
import 'models.dart';
import 'room_schedule.dart';
import 'room_widgets.dart';
import 'rooms_providers.dart';

/// Room detail (plan §13) with the room's day schedule (UC03). Pull to refresh reloads the schedule.
class RoomDetailScreen extends ConsumerWidget {
  const RoomDetailScreen({super.key, required this.id});

  static const notFound = 'Room not found';

  /// Null when the route's id is not a number.
  final int? id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final id = this.id;
    if (id == null) return const _Frame(title: 'Room', child: MessageView(icon: Icons.search_off, message: notFound));

    final room = ref.watch(roomDetailProvider(id));
    return switch (room) {
      AsyncValue(:final value?) => _Frame(title: value.code, child: _RoomDetails(value)),
      AsyncValue(:final error?) => _Frame(
          title: 'Room',
          child: switch (Problem.from(error)) {
            Problem(status: 404) => const MessageView(icon: Icons.search_off, message: notFound),
            _ => ErrorRetryView(error: error, onRetry: () => ref.invalidate(roomDetailProvider(id))),
          },
        ),
      _ => const _Frame(title: 'Room', child: Center(child: CircularProgressIndicator())),
    };
  }
}

class _Frame extends StatelessWidget {
  const _Frame({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) =>
      Scaffold(appBar: AppBar(title: Text(title)), body: SafeArea(child: child));
}

class _RoomDetails extends ConsumerWidget {
  const _RoomDetails(this.room);

  final Room room;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final textTheme = Theme.of(context).textTheme;
    final key = (roomId: room.id, date: ref.watch(scheduleDateProvider(room.id)));
    // Errors are shown by the schedule section from the error state.
    return RefreshIndicator(
      onRefresh: () => ref.refresh(roomScheduleProvider(key).future).then((_) {}, onError: (_) {}),
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.all(16),
        children: [
          Text(room.name, style: textTheme.headlineSmall),
          const SizedBox(height: 4),
          Text(room.code, style: textTheme.titleMedium),
          const SizedBox(height: 16),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.apartment_outlined),
            title: Text(room.building.name),
            subtitle: const Text('Building'),
          ),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.meeting_room_outlined),
            title: Text(RoomTypes.label(room.type)),
            subtitle: const Text('Type'),
          ),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.people_outline),
            title: Text('${room.capacity}'),
            subtitle: const Text('Capacity'),
          ),
          const SizedBox(height: 8),
          Text('Features', style: textTheme.titleSmall),
          const SizedBox(height: 8),
          room.features.isEmpty ? const Text('None listed') : FeatureChips(room.features),
          const SizedBox(height: 24),
          RoomScheduleSection(roomId: room.id),
        ],
      ),
    );
  }
}
