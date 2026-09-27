import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/format.dart';
import '../rooms/room_widgets.dart';
import 'models.dart';
import 'request_status.dart';
import 'requests_providers.dart';

/// Request detail (plan §13, UC06): what was asked for and the status timeline. The proposal and quote arrive in
/// Phase 3.
class RequestDetailScreen extends ConsumerWidget {
  const RequestDetailScreen({super.key, required this.id});

  static const unavailable = "This request isn't available";
  static const proposalPlaceholder =
      'The proposed room and quote will appear here once your request has been reviewed.';

  /// Null when the route's id is not a number.
  final int? id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final id = this.id;
    const notAvailable = MessageView(icon: Icons.lock_outline, message: unavailable);
    if (id == null) return const _Frame(child: notAvailable);

    final request = ref.watch(requestDetailProvider(id));
    return _Frame(
      child: switch (request) {
        AsyncValue(:final value?) => RefreshIndicator(
            onRefresh: () => ref.refresh(requestDetailProvider(id).future).then((_) {}, onError: (_) {}),
            child: _Details(value),
          ),
        AsyncValue(:final error?) => switch (Problem.from(error)) {
            // Someone else's request (403) and a missing one (404) look the same to the requester.
            Problem(status: 403 || 404) => notAvailable,
            final problem => MessageView(
                icon: Icons.error_outline,
                message: problem.title,
                action: FilledButton.tonal(
                  onPressed: () => ref.invalidate(requestDetailProvider(id)),
                  child: const Text('Retry'),
                ),
              ),
          },
        _ => const Center(child: CircularProgressIndicator()),
      },
    );
  }
}

class _Frame extends StatelessWidget {
  const _Frame({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) =>
      Scaffold(appBar: AppBar(title: const Text('Request')), body: SafeArea(child: child));
}

class _Details extends StatelessWidget {
  const _Details(this.request);

  final RequestDetail request;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    Widget info(IconData icon, String title, String label) => ListTile(
          contentPadding: EdgeInsets.zero,
          leading: Icon(icon),
          title: Text(title),
          subtitle: Text(label),
        );

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(16),
      children: [
        Text(request.purpose, style: textTheme.headlineSmall),
        const SizedBox(height: 8),
        Align(alignment: Alignment.centerLeft, child: StatusChip(request.status)),
        const SizedBox(height: 8),
        info(Icons.groups_outlined, request.club?.name ?? 'Academic booking', 'Club'),
        info(Icons.calendar_today_outlined, formatCampusDate(request.requestedStart), 'Date'),
        info(Icons.schedule, formatCampusTimeRange(request.requestedStart, request.requestedEnd), 'Time (campus)'),
        info(Icons.people_outline, '${request.attendees}', 'Attendees'),
        info(Icons.payments_outlined, formatLkr(request.budgetLkr), 'Budget'),
        const SizedBox(height: 8),
        Text('Features', style: textTheme.titleSmall),
        const SizedBox(height: 4),
        Text(request.requiredFeatures.isEmpty ? 'None' : request.requiredFeatures.map((f) => f.name).join(', ')),
        const SizedBox(height: 12),
        Text('Equipment', style: textTheme.titleSmall),
        const SizedBox(height: 4),
        if (request.equipment.isEmpty) const Text('None'),
        for (final line in request.equipment) Text('${line.quantity} × ${line.typeName}'),
        const SizedBox(height: 12),
        Text('Notes', style: textTheme.titleSmall),
        const SizedBox(height: 4),
        Text(request.notes ?? '—'),
        const SizedBox(height: 16),
        const Card(
          key: Key('request.proposalPlaceholder'),
          child: ListTile(
            leading: Icon(Icons.hourglass_empty),
            title: Text(RequestDetailScreen.proposalPlaceholder),
          ),
        ),
        const SizedBox(height: 16),
        Text('Status history', style: textTheme.titleMedium),
        const SizedBox(height: 8),
        StatusTimeline(history: request.history, requesterId: request.requester.id),
      ],
    );
  }
}

/// The status changes, oldest first: label, campus time, who made the change and the reason.
class StatusTimeline extends StatelessWidget {
  const StatusTimeline({super.key, required this.history, required this.requesterId});

  final List<StatusChange> history;
  final int requesterId;

  /// "You" for the requester (matched by id, since names are not unique), "System" when no user made the change.
  String _who(StatusChange change) => switch (change) {
        StatusChange(changedById: null) => 'System',
        StatusChange(:final changedById) when changedById == requesterId => 'You',
        StatusChange(:final changedByName) => changedByName ?? 'Staff',
      };

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final line = Theme.of(context).colorScheme.outlineVariant;
    return Column(
      children: [
        for (final (index, change) in history.indexed)
          IntrinsicHeight(
            key: Key('timeline.$index'),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SizedBox(
                  width: 24,
                  child: Column(
                    children: [
                      Container(
                        margin: const EdgeInsets.only(top: 4),
                        width: 12,
                        height: 12,
                        decoration: BoxDecoration(
                          color: RequestStatuses.color(change.toStatus),
                          shape: BoxShape.circle,
                        ),
                      ),
                      if (index < history.length - 1) Expanded(child: Container(width: 2, color: line)),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.only(bottom: 16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(RequestStatuses.label(change.toStatus), style: textTheme.titleSmall),
                        Text('${formatCampusDateTime(change.changedAt)} · ${_who(change)}', style: textTheme.bodySmall),
                        if (change.reason case final reason?) Text(reason, style: textTheme.bodyMedium),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}
