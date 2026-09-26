import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'facilities_repository.dart';
import 'models.dart';
import 'room_filter.dart';

// Riverpod 3 retries failing providers by default. These screens show the error with their own
// Retry button (and a 404 must show at once), so they never retry on their own.
Duration? _noRetry(int _, Object _) => null;

/// The Browse rooms filters. Reset when the rooms screen closes (auto-dispose).
class RoomFilterNotifier extends Notifier<RoomFilter> {
  static const searchDebounce = Duration(milliseconds: 300);

  Timer? _searchTimer;

  @override
  RoomFilter build() {
    ref.onDispose(() => _searchTimer?.cancel());
    return const RoomFilter();
  }

  /// Applies [text] once typing pauses, so each keystroke does not fetch.
  void setSearch(String text) {
    _searchTimer?.cancel();
    _searchTimer = Timer(searchDebounce, () => state = state.copyWith(search: text));
  }

  void setBuilding(int? id) => state = state.copyWith(buildingId: () => id);

  void setType(String? type) => state = state.copyWith(type: () => type);

  /// 0 means no minimum.
  void setMinCapacity(int value) => state = state.copyWith(minCapacity: value);

  void toggleFeature(String code) => state = state.copyWith(
        features: state.features.contains(code)
            ? ({...state.features}..remove(code))
            : {...state.features, code},
      );

  void clear() {
    _searchTimer?.cancel();
    state = const RoomFilter();
  }
}

final roomFilterProvider = NotifierProvider.autoDispose<RoomFilterNotifier, RoomFilter>(RoomFilterNotifier.new);

/// The rooms loaded so far for the current filter.
class RoomsPage {
  const RoomsPage({required this.items, required this.total, required this.page, this.isLoadingMore = false});

  final List<Room> items;

  /// Every match, not just the loaded ones.
  final int total;

  /// The last page loaded.
  final int page;
  final bool isLoadingMore;

  bool get hasMore => items.length < total;

  RoomsPage copyWith({List<Room>? items, int? total, int? page, bool? isLoadingMore}) => RoomsPage(
        items: items ?? this.items,
        total: total ?? this.total,
        page: page ?? this.page,
        isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      );
}

/// GET /api/rooms for the current filter, one page at a time. A filter change starts again from page 1.
class RoomsListNotifier extends AsyncNotifier<RoomsPage> {
  static const pageSize = 20;

  // Bumped on every (re)build, so a page that arrives after the filter changed is dropped.
  int _generation = 0;

  @override
  Future<RoomsPage> build() async {
    _generation++;
    final filter = ref.watch(roomFilterProvider);
    final result = await ref.read(facilitiesRepositoryProvider).getRooms(filter, page: 1, pageSize: pageSize);
    return RoomsPage(items: result.items, total: result.total, page: 1);
  }

  /// Appends the next page. Does nothing while loading or once everything is loaded.
  /// On failure the loaded rooms stay and the error is rethrown for the screen to show.
  Future<void> loadMore() async {
    final current = state.value;
    if (state.isLoading || current == null || current.isLoadingMore || !current.hasMore) return;

    final generation = _generation;
    final filter = ref.read(roomFilterProvider);
    state = AsyncData(current.copyWith(isLoadingMore: true));
    try {
      final next = await ref
          .read(facilitiesRepositoryProvider)
          .getRooms(filter, page: current.page + 1, pageSize: pageSize);
      if (!ref.mounted || generation != _generation) return;
      state = AsyncData(RoomsPage(items: [...current.items, ...next.items], total: next.total, page: next.page));
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

final roomsListProvider =
    AsyncNotifierProvider.autoDispose<RoomsListNotifier, RoomsPage>(RoomsListNotifier.new, retry: _noRetry);

/// Small reference lists, fetched once per session.
final buildingsProvider = FutureProvider<List<Building>>(
  (ref) => ref.watch(facilitiesRepositoryProvider).getBuildings(),
  retry: _noRetry,
);

final featuresProvider = FutureProvider<List<Feature>>(
  (ref) => ref.watch(facilitiesRepositoryProvider).getFeatures(),
  retry: _noRetry,
);

final roomDetailProvider = FutureProvider.autoDispose.family<Room, int>(
  (ref, id) => ref.watch(facilitiesRepositoryProvider).getRoom(id),
  retry: _noRetry,
);
