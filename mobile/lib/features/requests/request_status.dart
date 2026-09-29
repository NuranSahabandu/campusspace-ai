import 'package:flutter/material.dart';

/// Booking request statuses, mirroring backend Models/RequestStatuses.cs. The only place the app lists them, with
/// their friendly labels and colours.
abstract final class RequestStatuses {
  static const submitted = 'Submitted';
  static const agentProcessing = 'AgentProcessing';
  static const pendingApproval = 'PendingApproval';
  static const approved = 'Approved';
  static const completed = 'Completed';
  static const agentFailed = 'AgentFailed';
  static const revisionRequested = 'RevisionRequested';
  static const rejected = 'Rejected';
  static const cancelled = 'Cancelled';

  /// The statuses a requester can cancel from (mirrors the backend state machine's → Cancelled transitions).
  /// Processing, revision and failed requests are busy with the agent; the server answers them with a 409.
  static const cancellable = {submitted, pendingApproval, approved};

  /// The statuses the server moves on its own, and how often a screen showing one re-fetches until it changes: the
  /// agent's work (after submit, retry or an officer's revise) every 3 s, a proposal waiting for the Facilities Officer
  /// every 15 s. Any other status stops refreshing. The only list of them (the 3 s ones mirror web
  /// `REFRESHING_STATUSES`).
  static const refreshIntervals = {
    agentProcessing: Duration(seconds: 3),
    revisionRequested: Duration(seconds: 3),
    pendingApproval: Duration(seconds: 15),
  };

  /// How often to re-fetch a screen showing [status]; null means don't.
  static Duration? refreshIntervalFor(String status) => refreshIntervals[status];

  /// The shortest interval among [statuses] (a list screen), or null when none refreshes.
  static Duration? shortestRefreshInterval(Iterable<String> statuses) =>
      statuses.map(refreshIntervalFor).nonNulls.fold<Duration?>(null, (a, b) => a == null || b < a ? b : a);

  static String label(String status) => switch (status) {
        submitted => 'Submitted',
        agentProcessing => 'Processing',
        pendingApproval => 'Waiting for approval',
        approved => 'Approved',
        completed => 'Completed',
        revisionRequested => 'Needs revision',
        rejected => 'Rejected',
        cancelled => 'Cancelled',
        agentFailed => 'Failed',
        _ => status,
      };

  /// A distinct hue per status; StatusChip picks the shades for light and dark themes.
  static MaterialColor color(String status) => switch (status) {
        submitted => Colors.blue,
        agentProcessing => Colors.indigo,
        pendingApproval => Colors.amber,
        approved => Colors.green,
        completed => Colors.teal,
        revisionRequested => Colors.orange,
        rejected => Colors.red,
        cancelled => Colors.grey,
        agentFailed => Colors.brown,
        _ => Colors.blueGrey,
      };
}

/// The My requests filter chips, each sent as repeated `status` query parameters.
enum RequestStatusFilter {
  all('All', []),
  inProgress('In progress', [
    RequestStatuses.submitted,
    RequestStatuses.agentProcessing,
    RequestStatuses.pendingApproval,
    RequestStatuses.revisionRequested,
  ]),
  approved('Approved', [RequestStatuses.approved, RequestStatuses.completed]),
  closed('Closed', [RequestStatuses.rejected, RequestStatuses.cancelled, RequestStatuses.agentFailed]);

  const RequestStatusFilter(this.label, this.statuses);

  final String label;

  /// Empty means every status.
  final List<String> statuses;
}

/// A small coloured chip with the status's friendly label.
class StatusChip extends StatelessWidget {
  const StatusChip(this.status, {super.key});

  final String status;

  @override
  Widget build(BuildContext context) {
    final color = RequestStatuses.color(status);
    final dark = Theme.of(context).brightness == Brightness.dark;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: dark ? color.shade900.withValues(alpha: 0.6) : color.shade100,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Text(
        RequestStatuses.label(status),
        style: Theme.of(context).textTheme.labelMedium?.copyWith(color: dark ? color.shade100 : color.shade900),
      ),
    );
  }
}
