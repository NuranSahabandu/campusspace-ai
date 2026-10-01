import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/router.dart';
import '../auth/auth_controller.dart';
import '../auth/models.dart';
import '../requests/request_status.dart';
import '../requests/requests_providers.dart';
import '../requests/requests_repository.dart';
import 'status_notices.dart';
import 'status_notifier.dart';

/// The statuses that can still change on their own or by Facilities. A request that leaves them (Rejected,
/// Cancelled, Completed) is looked up once by id to learn how it ended, then forgotten.
const watchedStatuses = [
  RequestStatuses.submitted,
  RequestStatuses.agentProcessing,
  RequestStatuses.pendingApproval,
  RequestStatuses.revisionRequested,
  RequestStatuses.agentFailed,
  RequestStatuses.approved,
];

/// PageQuery's maximum. The open-request cap keeps a requester far below it.
const watchedPageSize = 100;

/// How often to poll when nothing is in flight (only Approved or AgentFailed requests, or none): an officer can still
/// cancel an approved booking or retry a failed one.
const idlePollInterval = Duration(seconds: 60);

/// Polls the signed-in requester's requests and shows a notice for each real status change (UC08). One instance per
/// sign-in; it lives only while the app process does (no background work, no stored state).
class StatusWatcher {
  StatusWatcher({required this.repository, required this.notifier, this.onChanged});

  final RequestsRepository repository;
  final StatusNotifier notifier;

  /// Called with the request id after each notice, so open screens can reload.
  final void Function(int requestId)? onChanged;

  final _baseline = StatusBaseline();
  Timer? _timer;
  bool _stopped = false;

  @visibleForTesting
  StatusBaseline get baseline => _baseline;

  void start() => unawaited(_tick());

  /// Stops polling and drops the baseline (logout or a user switch).
  void stop() {
    _stopped = true;
    _timer?.cancel();
    _baseline.clear();
  }

  Future<void> _tick() async {
    var next = idlePollInterval;
    try {
      next = await _poll() ?? idlePollInterval;
    } catch (_) {
      // Offline or a server error: keep the baseline and try again later. A 401 has already signed the user out.
    }
    if (!_stopped) _timer = Timer(next, () => unawaited(_tick()));
  }

  /// One poll. Returns the next interval (null = idle).
  Future<Duration?> _poll() async {
    final page = await repository.getRequests(watchedStatuses, page: 1, pageSize: watchedPageSize);
    if (_stopped) return null;
    final snapshots = page.items.map(RequestSnapshot.fromSummary).toList();
    final listed = {for (final s in snapshots) s.id};

    final ended = <int>[];
    for (final id in _baseline.ids.where((id) => !listed.contains(id)).toList()) {
      try {
        final detail = await repository.getRequest(id);
        if (_stopped) return null;
        snapshots.add(RequestSnapshot.fromDetail(detail));
        if (!watchedStatuses.contains(detail.status)) ended.add(id);
      } on DioException catch (e) {
        // Gone or no longer readable: stop tracking it. Anything else is tried again next poll.
        final status = e.response?.statusCode;
        if (status == 403 || status == 404) _baseline.forget(id);
      }
    }

    final notices = _baseline.update(snapshots);
    ended.forEach(_baseline.forget);
    for (final notice in notices) {
      if (_stopped) return null;
      await notifier.show(notice);
      onChanged?.call(notice.requestId);
    }
    return RequestStatuses.shortestRefreshInterval(page.items.map((r) => r.status));
  }
}

const _requesterRoles = {Roles.student, Roles.lecturer};

/// The app-level watcher: one while a Student or Lecturer is signed in, otherwise null. A sign-out or a different user
/// disposes it, which stops polling, clears the baseline and removes every shown notice. Watched by the app widget.
final statusWatcherProvider = Provider<StatusWatcher?>((ref) {
  final user = ref.watch(authControllerProvider.select((auth) => switch (auth.value) {
        Authenticated(:final user) => (id: user.id, role: user.role),
        _ => null,
      }));
  if (user == null || !_requesterRoles.contains(user.role)) return null;

  final notifier = ref.read(statusNotifierProvider);
  final watcher = StatusWatcher(
    repository: ref.read(requestsRepositoryProvider),
    notifier: notifier,
    onChanged: (id) {
      if (!ref.mounted) return;
      ref.invalidate(myRequestsProvider);
      ref.invalidate(requestDetailProvider(id));
    },
  );
  // A tapped notice opens the request (the router's guard still applies).
  final taps = notifier.taps.listen((id) {
    if (ref.mounted) ref.read(routerProvider).go(AppRoutes.request(id));
  });
  ref.onDispose(() {
    unawaited(taps.cancel());
    watcher.stop();
    unawaited(notifier.cancelAll());
  });
  watcher.start();
  return watcher;
});
