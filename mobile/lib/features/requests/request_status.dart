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
