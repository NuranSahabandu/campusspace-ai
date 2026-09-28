import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/format.dart';
import '../rooms/room_widgets.dart';
import 'models.dart';
import 'request_status.dart';
import 'requests_providers.dart';
import 'requests_repository.dart';
import 'time_rules.dart';

/// Request detail (plan §13, UC06): what was asked for and the status timeline, and the owner's Cancel (UC07). The
/// proposal and quote arrive in Phase 3.
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
        if (request.cancelledAt case final cancelledAt?) ...[
          const SizedBox(height: 8),
          CancellationInfo(request: request, cancelledAt: cancelledAt),
        ],
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
        CancelRequestButton(request: request),
        const SizedBox(height: 16),
        Text('Status history', style: textTheme.titleMedium),
        const SizedBox(height: 8),
        StatusTimeline(history: request.history, requesterId: request.requester.id),
      ],
    );
  }
}

/// When the request was cancelled, whether that was late (flagged, not charged) and whether the office did it.
class CancellationInfo extends StatelessWidget {
  const CancellationInfo({super.key, required this.request, required this.cancelledAt});

  static const lateChip = 'Late cancellation';
  static const byOfficer = 'Cancelled by the facilities office';

  final RequestDetail request;
  final DateTime cancelledAt;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      key: const Key('request.cancellation'),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Cancelled on ${formatCampusDateTime(cancelledAt)}'),
            if (request.cancelledByOfficer) ...[
              const SizedBox(height: 4),
              const Text(byOfficer),
            ],
            if (request.isLateCancellation) ...[
              const SizedBox(height: 8),
              Chip(
                avatar: Icon(Icons.schedule, size: 18, color: scheme.onErrorContainer),
                label: Text(lateChip, style: TextStyle(color: scheme.onErrorContainer)),
                backgroundColor: scheme.errorContainer,
                side: BorderSide.none,
                visualDensity: VisualDensity.compact,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// The owner's Cancel button, shown only for a cancellable status (RequestStatuses.cancellable). Asks for
/// confirmation and an optional reason, then refreshes the detail and My requests. The server decides whether it is
/// late; a 409 shows its message as sent.
class CancelRequestButton extends ConsumerStatefulWidget {
  const CancelRequestButton({super.key, required this.request});

  static const label = 'Cancel request';
  static const cancelled = 'Request cancelled';
  static const cancelledLate = 'Request cancelled. It was recorded as a late cancellation.';

  final RequestDetail request;

  @override
  ConsumerState<CancelRequestButton> createState() => _CancelRequestButtonState();
}

class _CancelRequestButtonState extends ConsumerState<CancelRequestButton> {
  bool _busy = false;

  Future<void> _cancel() async {
    final request = widget.request;
    final reason = await showDialog<String>(context: context, builder: (_) => CancelRequestDialog(request: request));
    if (reason == null || !mounted) return;

    // The button disappears once the request is cancelled, so keep what is needed after the call.
    final container = ProviderScope.containerOf(context, listen: false);
    final messenger = ScaffoldMessenger.of(context);
    void show(String text) => messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(text)));

    setState(() => _busy = true);
    try {
      final result = await container.read(requestsRepositoryProvider).cancel(request.id, reason: reason);
      container
        ..invalidate(requestDetailProvider(request.id))
        ..invalidate(myRequestsProvider)
        ..invalidate(eligibilityProvider);
      show(result.isLateCancellation ? CancelRequestButton.cancelledLate : CancelRequestButton.cancelled);
    } catch (e) {
      final problem = Problem.from(e);
      if (problem.status == 409) {
        // The status changed under us (for example the agent picked it up): show the server's words and the
        // current state.
        container.invalidate(requestDetailProvider(request.id));
        show(problem.title);
      } else {
        show(problem.fieldError('reason') ?? problem.title);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final request = widget.request;
    final isOwner = ref.watch(currentUserIdProvider) == request.requester.id;
    if (!isOwner || !RequestStatuses.cancellable.contains(request.status)) return const SizedBox.shrink();

    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(top: 16),
      child: OutlinedButton.icon(
        key: const Key('request.cancel'),
        onPressed: _busy ? null : _cancel,
        style: OutlinedButton.styleFrom(foregroundColor: scheme.error, side: BorderSide(color: scheme.error)),
        icon: _busy
            ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
            : const Icon(Icons.cancel_outlined),
        label: const Text(CancelRequestButton.label),
      ),
    );
  }
}

/// Confirms a cancel. Pops the typed reason (possibly empty) to confirm, or null to keep the request. Warns when an
/// approved request is inside the late window, read from the live policy; the server still decides.
class CancelRequestDialog extends ConsumerStatefulWidget {
  const CancelRequestDialog({super.key, required this.request});

  static const title = 'Cancel this request?';
  static const lateWarning = 'This is a late cancellation. It will be recorded as late.';
  static const keep = 'Keep request';

  /// CancelBookingRequestRequest.Reason's [MaxLength].
  static const reasonMaxLength = 500;

  final RequestDetail request;

  @override
  ConsumerState<CancelRequestDialog> createState() => _CancelRequestDialogState();
}

class _CancelRequestDialogState extends ConsumerState<CancelRequestDialog> {
  final _reason = TextEditingController();

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final request = widget.request;
    final scheme = Theme.of(context).colorScheme;
    // Only an approved request can be late, so only then is the policy needed.
    final policy = request.status == RequestStatuses.approved ? ref.watch(policyProvider).value : null;
    final late = policy != null &&
        isLateCancellation(
          status: request.status,
          startUtc: request.requestedStart,
          nowUtc: ref.watch(clockProvider)(),
          policy: policy,
        );

    return AlertDialog(
      title: const Text(CancelRequestDialog.title),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('${request.purpose}\n${formatCampusSlot(request.requestedStart, request.requestedEnd)}'),
            if (late) ...[
              const SizedBox(height: 12),
              Container(
                key: const Key('cancel.lateWarning'),
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(color: scheme.errorContainer, borderRadius: BorderRadius.circular(8)),
                child: Row(
                  children: [
                    Icon(Icons.warning_amber, color: scheme.onErrorContainer),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(CancelRequestDialog.lateWarning, style: TextStyle(color: scheme.onErrorContainer)),
                    ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 12),
            TextField(
              key: const Key('cancel.reason'),
              controller: _reason,
              maxLength: CancelRequestDialog.reasonMaxLength,
              minLines: 1,
              maxLines: 4,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Reason (optional)', border: OutlineInputBorder()),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text(CancelRequestDialog.keep)),
        FilledButton(
          key: const Key('cancel.confirm'),
          style: FilledButton.styleFrom(backgroundColor: scheme.error, foregroundColor: scheme.onError),
          onPressed: () => Navigator.of(context).pop(_reason.text),
          child: const Text(CancelRequestButton.label),
        ),
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
