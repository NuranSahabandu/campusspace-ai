import '../requests/models.dart';
import '../requests/request_status.dart';

/// What the status watcher knows about one of the user's requests. Built from a list row or, once the request has
/// left the watched statuses, from its detail.
class RequestSnapshot {
  const RequestSnapshot({
    required this.id,
    required this.purpose,
    required this.status,
    this.cancelledByOfficer = false,
    this.closedBySystem = false,
  });

  factory RequestSnapshot.fromSummary(RequestSummary r) =>
      RequestSnapshot(id: r.id, purpose: r.purpose, status: r.status, cancelledByOfficer: r.cancelledByOfficer);

  /// A Rejected history row with no actor was closed by the system ("Closed automatically"), not by an officer.
  factory RequestSnapshot.fromDetail(RequestDetail r) => RequestSnapshot(
        id: r.id,
        purpose: r.purpose,
        status: r.status,
        cancelledByOfficer: r.cancelledByOfficer,
        closedBySystem: r.status == RequestStatuses.rejected &&
            r.lastChangeTo(RequestStatuses.rejected)?.changedById == null,
      );

  final int id;
  final String purpose;
  final String status;
  final bool cancelledByOfficer;
  final bool closedBySystem;
}

/// The status changes worth a notification. PendingApproval is deliberately not one: the requester has nothing to
/// do, it would fire again after every revise, and the request screen already shows the proposal.
enum NoticeKind { approved, rejected, closed, revision, failed, cancelledByOfficer }

class StatusNotice {
  const StatusNotice({required this.requestId, required this.kind, required this.purpose});

  final int requestId;
  final NoticeKind kind;
  final String purpose;

  @override
  bool operator ==(Object other) =>
      other is StatusNotice && other.requestId == requestId && other.kind == kind && other.purpose == purpose;

  @override
  int get hashCode => Object.hash(requestId, kind, purpose);

  @override
  String toString() => 'StatusNotice($requestId, $kind)';
}

/// The notice for a change from [before] to [after], or null when the change isn't one the requester is told about
/// (their own cancel, a submit or retry starting the agent, a new proposal, completion).
NoticeKind? noticeFor(RequestSnapshot before, RequestSnapshot after) {
  if (before.status == after.status) return null;
  return switch (after.status) {
    RequestStatuses.approved => NoticeKind.approved,
    RequestStatuses.rejected => after.closedBySystem ? NoticeKind.closed : NoticeKind.rejected,
    RequestStatuses.revisionRequested => NoticeKind.revision,
    // RevisionRequested lasts only until the agent restarts (well under the 15 s PendingApproval poll), so a revise
    // (officer or failed approval) is usually seen as PendingApproval → AgentProcessing.
    RequestStatuses.agentProcessing when before.status == RequestStatuses.pendingApproval => NoticeKind.revision,
    RequestStatuses.agentFailed => NoticeKind.failed,
    RequestStatuses.cancelled when after.cancelledByOfficer => NoticeKind.cancelledByOfficer,
    _ => null,
  };
}

/// The last status seen per request, in memory only. The first [update] after sign-in takes the baseline and notifies
/// nothing; later updates return one notice per real change, never the same change twice.
class StatusBaseline {
  final Map<int, RequestSnapshot> _seen = {};
  bool _hasBaseline = false;

  bool get hasBaseline => _hasBaseline;

  /// The ids currently tracked.
  Iterable<int> get ids => _seen.keys;

  /// Records [snapshots] and returns the notices for the ones whose status changed. Unknown ids (a new request) are
  /// recorded silently.
  List<StatusNotice> update(Iterable<RequestSnapshot> snapshots) {
    final notices = <StatusNotice>[];
    for (final now in snapshots) {
      final before = _seen[now.id];
      _seen[now.id] = now;
      if (!_hasBaseline || before == null) continue;
      final kind = noticeFor(before, now);
      if (kind != null) notices.add(StatusNotice(requestId: now.id, kind: kind, purpose: now.purpose));
    }
    _hasBaseline = true;
    return notices;
  }

  /// Stops tracking [id] (the request can no longer change, or the user can no longer read it).
  void forget(int id) => _seen.remove(id);

  void clear() {
    _seen.clear();
    _hasBaseline = false;
  }
}

const purposeMaxLength = 40;

/// The purpose as one short line of plain text: control characters removed, whitespace collapsed, at most
/// [purposeMaxLength] characters (then "…").
String safePurpose(String purpose) {
  final clean = purpose.replaceAll(RegExp(r'[\x00-\x1F\x7F-\x9F]'), ' ').replaceAll(RegExp(r'\s+'), ' ').trim();
  final chars = clean.runes.toList();
  if (chars.length <= purposeMaxLength) return clean;
  return '${String.fromCharCodes(chars.take(purposeMaxLength - 1)).trimRight()}…';
}

/// Fixed templates: the purpose is the only variable part. Never ids, notes, reasons or agent output.
({String title, String body}) noticeText(StatusNotice notice) {
  final p = '"${safePurpose(notice.purpose)}"';
  return switch (notice.kind) {
    NoticeKind.approved => (title: 'Booking approved', body: '$p was approved. Tap to see the details.'),
    NoticeKind.rejected => (title: 'Request rejected', body: '$p was rejected by Facilities.'),
    NoticeKind.closed => (title: 'Request closed', body: '$p was closed automatically.'),
    NoticeKind.revision => (title: 'Request being re-planned', body: '$p is being re-planned.'),
    NoticeKind.failed => (title: "Couldn't prepare a proposal", body: 'Facilities can retry $p.'),
    NoticeKind.cancelledByOfficer => (title: 'Booking cancelled', body: '$p was cancelled by Facilities.'),
  };
}
