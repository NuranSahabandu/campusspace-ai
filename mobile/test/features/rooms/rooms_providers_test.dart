import 'dart:async';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/core/campus_time.dart';
import 'package:campusspace_mobile/features/rooms/facilities_repository.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:campusspace_mobile/features/rooms/room_filter.dart';
import 'package:campusspace_mobile/features/rooms/rooms_providers.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockFacilitiesRepository repository;

  setUpAll(() {
    registerFallbackValue(const RoomFilter());
    registerFallbackValue(DateTime.utc(2026));
  });

  setUp(() => repository = MockFacilitiesRepository());

  ProviderContainer createContainer() {
    final container = ProviderContainer(overrides: [facilitiesRepositoryProvider.overrideWithValue(repository)]);
    addTearDown(container.dispose);
    // Keep the auto-dispose providers alive, as the screen would.
    container.listen(roomsListProvider, (_, _) {});
    container.listen(roomFilterProvider, (_, _) {});
    return container;
  }

  void stubRooms(Future<PagedResult<Room>> Function(RoomFilter filter, int page) answer) => when(
    () => repository.getRooms(
      any(),
      page: any(named: 'page'),
      pageSize: any(named: 'pageSize'),
    ),
  ).thenAnswer((i) => answer(i.positionalArguments[0] as RoomFilter, i.namedArguments[#page] as int));

  test('loadMore appends the next page and stops at total', () async {
    stubRooms((_, page) async => roomsPage(page, 25));
    final container = createContainer();

    final first = await container.read(roomsListProvider.future);
    expect(first.items, hasLength(20));
    expect(first.hasMore, isTrue);

    await container.read(roomsListProvider.notifier).loadMore();
    await container.read(roomsListProvider.notifier).loadMore();

    final all = container.read(roomsListProvider).value!;
    expect(all.items.map((r) => r.code).last, 'R25');
    expect(all.items, hasLength(25));
    expect(all.hasMore, isFalse);
    verify(() => repository.getRooms(any(), page: 2, pageSize: 20)).called(1);
    verifyNever(() => repository.getRooms(any(), page: 3, pageSize: any(named: 'pageSize')));
  });

  test('a filter change starts again from page 1 and drops a late page', () async {
    final page2 = Completer<PagedResult<Room>>();
    stubRooms((filter, page) => page == 2 ? page2.future : Future.value(roomsPage(page, filter.isEmpty ? 25 : 3)));
    final container = createContainer();
    await container.read(roomsListProvider.future);

    final loading = container.read(roomsListProvider.notifier).loadMore();
    expect(container.read(roomsListProvider).value!.isLoadingMore, isTrue);

    container.read(roomFilterProvider.notifier).toggleFeature('computers');
    final filtered = await container.read(roomsListProvider.future);
    page2.complete(roomsPage(2, 25));
    await loading;

    expect(filtered.items, hasLength(3));
    expect(container.read(roomsListProvider).value!.items, hasLength(3));
    verify(() => repository.getRooms(const RoomFilter(features: {'computers'}), page: 1, pageSize: 20)).called(1);
  });

  test('a failed loadMore keeps the loaded rooms and rethrows', () async {
    stubRooms((_, page) async => page == 1 ? roomsPage(1, 25) : throw httpError('/api/rooms', 500));
    final container = createContainer();
    await container.read(roomsListProvider.future);

    await expectLater(container.read(roomsListProvider.notifier).loadMore(), throwsA(anything));

    final state = container.read(roomsListProvider).value!;
    expect(state.items, hasLength(20));
    expect(state.isLoadingMore, isFalse);
  });

  // testWidgets runs on a fake clock that tester.pump advances.
  testWidgets('search is applied 300 ms after the last keystroke', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    container.listen(roomFilterProvider, (_, _) {});
    final filter = container.read(roomFilterProvider.notifier);

    filter.setSearch('A3');
    await tester.pump(const Duration(milliseconds: 200));
    filter.setSearch('A30');
    await tester.pump(const Duration(milliseconds: 299));
    expect(container.read(roomFilterProvider).search, '');

    await tester.pump(const Duration(milliseconds: 1));
    expect(container.read(roomFilterProvider).search, 'A30');
  });

  testWidgets('clear cancels a pending search and resets every filter', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    container.listen(roomFilterProvider, (_, _) {});
    final filter = container.read(roomFilterProvider.notifier)
      ..setBuilding(2)
      ..setMinCapacity(45)
      ..toggleFeature('projector')
      ..setSearch('lab');

    filter.clear();
    await tester.pump(const Duration(seconds: 1));

    expect(container.read(roomFilterProvider), const RoomFilter());
  });

  group('schedule', () {
    // 23:00 UTC on Sun 27 Sep is already Monday 04:30 on campus.
    final lateSunday = DateTime.utc(2026, 9, 27, 23);

    ProviderContainer scheduleContainer() {
      final container = ProviderContainer(overrides: [
        facilitiesRepositoryProvider.overrideWithValue(repository),
        clockProvider.overrideWithValue(() => lateSunday),
      ]);
      addTearDown(container.dispose);
      container.listen(scheduleDateProvider(3), (_, _) {});
      return container;
    }

    test('the date starts at campus today, not the UTC date, and moves by whole days', () {
      final container = scheduleContainer();
      final dates = container.read(scheduleDateProvider(3).notifier);

      expect(container.read(scheduleDateProvider(3)), DateTime.utc(2026, 9, 28));
      dates.move(1);
      expect(container.read(scheduleDateProvider(3)), DateTime.utc(2026, 9, 29));
      dates.move(-1);
      expect(container.read(scheduleDateProvider(3)), DateTime.utc(2026, 9, 28));
      // The picker returns local midnight; only its year, month and day count.
      dates.select(DateTime(2026, 10, 4));
      expect(container.read(scheduleDateProvider(3)), DateTime.utc(2026, 10, 4));
    });

    test('roomScheduleProvider is keyed by (roomId, date)', () async {
      when(() => repository.getSchedule(any(), any())).thenAnswer((_) async => RoomSchedule(
            date: DateTime.utc(2026, 9, 28),
            granularityMinutes: 30,
          ));
      final container = scheduleContainer();
      final key = (roomId: 3, date: DateTime.utc(2026, 9, 28));
      container.listen(roomScheduleProvider(key), (_, _) {});

      await container.read(roomScheduleProvider(key).future);
      // An equal record is the same entry: no second request.
      await container.read(roomScheduleProvider((roomId: 3, date: DateTime.utc(2026, 9, 28))).future);

      verify(() => repository.getSchedule(3, DateTime.utc(2026, 9, 28))).called(1);
    });
  });
}
