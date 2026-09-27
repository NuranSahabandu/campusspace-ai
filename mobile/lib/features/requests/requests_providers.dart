import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import 'models.dart';
import 'request_status.dart';
import 'requests_repository.dart';

// Riverpod 3 retries failing providers by default. These screens show the error with their own Retry button (and a
// 403 must show at once), so they never retry on their own.
Duration? _noRetry(int _, Object _) => null;

/// The signed-in user's role (null when signed out). Picks the advance window for the date picker.
final requesterRoleProvider = Provider<String?>((ref) => switch (ref.watch(authControllerProvider).value) {
      Authenticated(:final user) => user.role,
      _ => null,
    });

/// Whether the caller can submit now, their clubs and open-request count. Invalidated after a submit.
final eligibilityProvider = FutureProvider.autoDispose<Eligibility>(
  (ref) => ref.watch(requestsRepositoryProvider).getEligibility(),
  retry: _noRetry,
);

/// The live booking policy that guides the date and time pickers.
final policyProvider = FutureProvider.autoDispose<PublicPolicy>(
  (ref) => ref.watch(requestsRepositoryProvider).getPolicy(),
  retry: _noRetry,
);

final equipmentTypesProvider = FutureProvider.autoDispose<List<EquipmentType>>(
  (ref) => ref.watch(requestsRepositoryProvider).getEquipmentTypes(),
  retry: _noRetry,
);

/// The My requests filter chip. Reset when the screen closes (auto-dispose).
class RequestStatusFilterNotifier extends Notifier<RequestStatusFilter> {
  @override
  RequestStatusFilter build() => RequestStatusFilter.all;

  void select(RequestStatusFilter filter) => state = filter;
}

final requestStatusFilterProvider =
    NotifierProvider.autoDispose<RequestStatusFilterNotifier, RequestStatusFilter>(RequestStatusFilterNotifier.new);

/// The requests loaded so far for the current filter.
class RequestsPage {
  const RequestsPage({required this.items, required this.total, required this.page, this.isLoadingMore = false});

  final List<RequestSummary> items;

  /// Every match, not just the loaded ones.
  final int total;

  /// The last page loaded.
  final int page;
  final bool isLoadingMore;

  bool get hasMore => items.length < total;

  RequestsPage copyWith({bool? isLoadingMore}) =>
      RequestsPage(items: items, total: total, page: page, isLoadingMore: isLoadingMore ?? this.isLoadingMore);
}

/// GET /api/booking-requests for the current filter, one page at a time. A filter change starts again from page 1.
class MyRequestsNotifier extends AsyncNotifier<RequestsPage> {
  static const pageSize = 20;

  // Bumped on every (re)build, so a page that arrives after the filter changed is dropped.
  int _generation = 0;

  @override
  Future<RequestsPage> build() async {
    _generation++;
    final filter = ref.watch(requestStatusFilterProvider);
    final result =
        await ref.read(requestsRepositoryProvider).getRequests(filter.statuses, page: 1, pageSize: pageSize);
    return RequestsPage(items: result.items, total: result.total, page: 1);
  }

  /// Appends the next page. Does nothing while loading or once everything is loaded.
  /// On failure the loaded requests stay and the error is rethrown for the screen to show.
  Future<void> loadMore() async {
    final current = state.value;
    if (state.isLoading || current == null || current.isLoadingMore || !current.hasMore) return;

    final generation = _generation;
    final filter = ref.read(requestStatusFilterProvider);
    state = AsyncData(current.copyWith(isLoadingMore: true));
    try {
      final next = await ref
          .read(requestsRepositoryProvider)
          .getRequests(filter.statuses, page: current.page + 1, pageSize: pageSize);
      if (!ref.mounted || generation != _generation) return;
      state = AsyncData(RequestsPage(items: [...current.items, ...next.items], total: next.total, page: next.page));
    } catch (_) {
      if (ref.mounted && generation == _generation) state = AsyncData(current);
      rethrow;
    }
  }

  /// Reloads page 1 (pull-to-refresh). Errors end up in the state, not here.
  Future<void> refresh() async {
    ref.invalidateSelf();
    try {
      await future;
    } catch (_) {
      // Shown by the screen from the error state.
    }
  }
}

final myRequestsProvider =
    AsyncNotifierProvider.autoDispose<MyRequestsNotifier, RequestsPage>(MyRequestsNotifier.new, retry: _noRetry);

final requestDetailProvider = FutureProvider.autoDispose.family<RequestDetail, int>(
  (ref, id) => ref.watch(requestsRepositoryProvider).getRequest(id),
  retry: _noRetry,
);
