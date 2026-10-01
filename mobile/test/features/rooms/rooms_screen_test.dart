import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:campusspace_mobile/features/rooms/room_detail_screen.dart';
import 'package:campusspace_mobile/features/rooms/room_filter.dart';
import 'package:campusspace_mobile/features/rooms/room_widgets.dart';
import 'package:campusspace_mobile/features/rooms/rooms_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockFacilitiesRepository repository;

  setUpAll(() => registerFallbackValue(const RoomFilter()));

  setUp(() {
    repository = MockFacilitiesRepository();
    when(() => repository.getBuildings()).thenAnswer((_) async => const [
          Building(id: 1, code: 'MB', name: 'Main Building', isActive: true),
          Building(id: 2, code: 'NB', name: 'New Building', isActive: true),
        ]);
    when(() => repository.getFeatures()).thenAnswer((_) async => const [
          Feature(id: 1, code: 'computers', name: 'Computers'),
          Feature(id: 2, code: 'projector', name: 'Projector'),
        ]);
  });

  void stubRooms(Future<PagedResult<Room>> Function(RoomFilter filter, int page) answer) =>
      when(() => repository.getRooms(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((i) => answer(i.positionalArguments[0] as RoomFilter, i.namedArguments[#page] as int));

  final scrollable = find.byType(Scrollable).first;

  testWidgets('shows the rooms from the repository', (tester) async {
    stubRooms((_, _) async => PagedResult(
          items: [testRoom(1, features: const [FeatureRef(code: 'projector', name: 'Projector')])],
          page: 1,
          pageSize: 20,
          total: 1,
        ));
    await pumpRoomsScreens(tester, repository);

    expect(find.text('R1 · Room 1'), findsOneWidget);
    expect(find.text('Main Building · Seminar room'), findsOneWidget);
    expect(find.descendant(of: find.byType(CapacityLabel), matching: find.byIcon(Icons.people_outline)), findsOneWidget);
    expect(find.descendant(of: find.byType(CapacityLabel), matching: find.text('11')), findsOneWidget);
    expect(find.descendant(of: find.byType(RoomCard), matching: find.text('Projector')), findsOneWidget);
    verify(() => repository.getRooms(const RoomFilter(), page: 1, pageSize: 20)).called(1);
  });

  testWidgets('scrolling to the end loads page 2 and stops at total', (tester) async {
    stubRooms((_, page) async => roomsPage(page, 25));
    await pumpRoomsScreens(tester, repository);

    await tester.scrollUntilVisible(find.text('R25 · Room 25'), 300, scrollable: scrollable);
    await tester.pumpAndSettle();
    await tester.fling(scrollable, const Offset(0, -2000), 3000);
    await tester.pumpAndSettle();

    expect(find.text('R25 · Room 25'), findsOneWidget);
    verify(() => repository.getRooms(any(), page: 2, pageSize: 20)).called(1);
    verifyNever(() => repository.getRooms(any(), page: 3, pageSize: any(named: 'pageSize')));
  });

  testWidgets('selecting a feature chip refetches page 1 with that feature', (tester) async {
    stubRooms((filter, page) async => filter.features.isEmpty ? roomsPage(1, 3) : roomsPage(1, 1));
    await pumpRoomsScreens(tester, repository);
    expect(find.byType(RoomCard), findsNWidgets(3));

    await tester.tap(find.widgetWithText(FilterChip, 'Computers'));
    await tester.pumpAndSettle();

    verify(() => repository.getRooms(const RoomFilter(features: {'computers'}), page: 1, pageSize: 20)).called(1);
    expect(find.byType(RoomCard), findsOneWidget);
    expect(tester.widget<FilterChip>(find.widgetWithText(FilterChip, 'Computers')).selected, isTrue);
  });

  testWidgets('typing in search fetches once, 300 ms after the last keystroke', (tester) async {
    stubRooms((_, _) async => roomsPage(1, 3));
    await pumpRoomsScreens(tester, repository);

    await tester.enterText(find.byKey(const Key('rooms.search')), 'A3');
    await tester.pump(const Duration(milliseconds: 100));
    await tester.enterText(find.byKey(const Key('rooms.search')), 'A30');
    await tester.pump(const Duration(milliseconds: 299));
    verifyNever(() => repository.getRooms(const RoomFilter(search: 'A3'), page: 1, pageSize: 20));

    await tester.pumpAndSettle();
    verify(() => repository.getRooms(const RoomFilter(search: 'A30'), page: 1, pageSize: 20)).called(1);
    verifyNever(() => repository.getRooms(const RoomFilter(search: 'A3'), page: 1, pageSize: 20));
  });

  testWidgets('empty state: Clear filters resets the filter and refetches', (tester) async {
    stubRooms((filter, _) async => filter.isEmpty ? roomsPage(1, 2) : roomsPage(1, 0));
    await pumpRoomsScreens(tester, repository);

    await tester.tap(find.widgetWithText(FilterChip, 'Projector'));
    await tester.pumpAndSettle();
    expect(find.text(RoomsScreen.noMatches), findsOneWidget);

    await tester.tap(find.widgetWithText(FilledButton, 'Clear filters'));
    await tester.pumpAndSettle();

    expect(find.text(RoomsScreen.noMatches), findsNothing);
    expect(find.byType(RoomCard), findsNWidgets(2));
    expect(tester.widget<FilterChip>(find.widgetWithText(FilterChip, 'Projector')).selected, isFalse);
    verify(() => repository.getRooms(const RoomFilter(), page: 1, pageSize: 20)).called(2);
  });

  testWidgets('error state shows the Problem title; Retry refetches', (tester) async {
    var calls = 0;
    stubRooms((_, _) async {
      if (calls++ == 0) throw httpError('/api/rooms', 500, body: {'title': 'Server error', 'status': 500});
      return roomsPage(1, 2);
    });
    await pumpRoomsScreens(tester, repository);
    expect(find.text('Server error'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilledButton, 'Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Server error'), findsNothing);
    expect(find.byType(RoomCard), findsNWidgets(2));
    expect(calls, 2);
  });

  testWidgets('tapping a card opens the room detail', (tester) async {
    stubRooms((_, _) async => roomsPage(1, 2));
    when(() => repository.getRoom(2)).thenAnswer((_) async => testRoom(2));
    await pumpRoomsScreens(tester, repository);

    await tester.tap(find.text('R2 · Room 2'));
    await tester.pumpAndSettle();

    expect(find.byType(RoomDetailScreen), findsOneWidget);
    expect(find.text('Room 2'), findsOneWidget);
  });

  testWidgets('filter options that fail to load say so with Retry; the rooms still show', (tester) async {
    var calls = 0;
    when(() => repository.getFeatures()).thenAnswer((_) async {
      if (calls++ == 0) throw httpError('/api/features', 503, body: {'title': 'Service unavailable'});
      return const [Feature(id: 2, code: 'projector', name: 'Projector')];
    });
    stubRooms((_, _) async => roomsPage(1, 2));
    await pumpRoomsScreens(tester, repository);

    expect(find.text('${RoomsScreen.filtersFailed}: Service unavailable'), findsOneWidget);
    expect(find.byType(RoomCard), findsNWidgets(2));

    await tester.tap(find.descendant(of: find.byKey(const Key('rooms.filtersError')), matching: find.text('Retry')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('rooms.filtersError')), findsNothing);
    expect(find.widgetWithText(FilterChip, 'Projector'), findsOneWidget);
  });
}
