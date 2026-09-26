import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:campusspace_mobile/features/rooms/room_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockFacilitiesRepository repository;

  setUp(() => repository = MockFacilitiesRepository());

  testWidgets('shows the room, its features and the Phase 2 schedule note', (tester) async {
    when(() => repository.getRoom(5)).thenAnswer((_) async => const Room(
          id: 5,
          code: 'A301',
          name: 'Computer Lab A301',
          type: RoomTypes.computerLab,
          capacity: 48,
          isActive: true,
          building: BuildingRef(id: 1, code: 'MB', name: 'Main Building'),
          features: [
            FeatureRef(code: 'computers', name: 'Computers'),
            FeatureRef(code: 'projector', name: 'Projector'),
          ],
        ));
    await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/5');

    expect(find.text('Computer Lab A301'), findsOneWidget);
    expect(find.text('Main Building'), findsOneWidget);
    expect(find.text('Computer lab'), findsOneWidget);
    expect(find.text('48'), findsOneWidget);
    expect(find.widgetWithText(Chip, 'Computers'), findsOneWidget);
    expect(find.widgetWithText(Chip, 'Projector'), findsOneWidget);
    expect(find.text(RoomDetailScreen.schedulePlaceholder), findsOneWidget);
  });

  testWidgets('404 shows "Room not found" without Retry', (tester) async {
    when(() => repository.getRoom(99)).thenThrow(httpError('/api/rooms/99', 404));
    await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/99');

    expect(find.text(RoomDetailScreen.notFound), findsOneWidget);
    expect(find.text('Retry'), findsNothing);
  });

  testWidgets('a non-numeric id shows "Room not found" and calls nothing', (tester) async {
    await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/abc');

    expect(find.text(RoomDetailScreen.notFound), findsOneWidget);
    verifyNever(() => repository.getRoom(any()));
  });

  testWidgets('another error shows its title; Retry refetches', (tester) async {
    var calls = 0;
    when(() => repository.getRoom(5)).thenAnswer((_) async {
      if (calls++ == 0) throw httpError('/api/rooms/5', 503, body: {'title': 'Service unavailable'});
      return testRoom(5);
    });
    await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/5');
    expect(find.text('Service unavailable'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilledButton, 'Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Room 5'), findsOneWidget);
    expect(calls, 2);
  });
}
