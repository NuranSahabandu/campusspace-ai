import 'dart:convert';

import 'package:campusspace_mobile/core/campus_time.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:campusspace_mobile/features/rooms/room_detail_screen.dart';
import 'package:campusspace_mobile/features/rooms/room_schedule.dart';
import 'package:campusspace_mobile/core/ui/error_retry_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/rooms_schedule.dart';
import '../../helpers.dart';

RoomSchedule _schedule(String json) => RoomSchedule.fromJson(jsonDecode(json) as Map<String, dynamic>);

/// The captured A101 schedules by campus date; any other date is an open day with nothing booked.
RoomSchedule _scheduleOn(DateTime date) => switch (campusDateParam(date)) {
      '2026-09-28' => _schedule(scheduleBusyJson),
      '2026-10-04' => _schedule(scheduleClosedJson),
      '2026-10-06' => _schedule(scheduleFullJson),
      _ => RoomSchedule(
          date: date,
          open: const TimeOfDay(hour: 8, minute: 0),
          close: const TimeOfDay(hour: 20, minute: 0),
          granularityMinutes: 30,
          free: [
            FreeInterval(
              start: campusInstant(date, const TimeOfDay(hour: 8, minute: 0)),
              end: campusInstant(date, const TimeOfDay(hour: 20, minute: 0)),
            ),
          ],
        ),
    };

/// 10:00 campus time on [date].
DateTime _campusMorning(DateTime date) => campusInstant(date, const TimeOfDay(hour: 10, minute: 0));

void main() {
  late MockFacilitiesRepository repository;

  setUpAll(() => registerFallbackValue(DateTime.utc(2026)));

  setUp(() {
    repository = MockFacilitiesRepository();
    when(() => repository.getSchedule(any(), any()))
        .thenAnswer((i) async => _scheduleOn(i.positionalArguments[1] as DateTime));
  });

  /// A tall surface so the whole schedule is built.
  void tallSurface(WidgetTester tester) {
    tester.view.physicalSize = const Size(900, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  Finder entry(int i) => find.byKey(Key('schedule.entry.$i'));

  Finder inEntry(int i, String text) => find.descendant(of: entry(i), matching: find.text(text));

  testWidgets('shows the room and its features', (tester) async {
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
    expect(find.text(RoomScheduleSection.title), findsOneWidget);
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

  group('day schedule', () {
    testWidgets("shows today's hours, the blackout, the booking and the free time in campus time", (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/3');

      verify(() => repository.getSchedule(3, DateTime.utc(2026, 9, 28))).called(1);
      expect(find.text('Mon 28 Sep 2026'), findsOneWidget);
      expect(find.text('Open 08:00–20:00'), findsOneWidget);
      // The API sends UTC ("02:30Z"); rows show campus time (+05:30) whatever the device's zone.
      expect(inEntry(0, '08:00–12:00'), findsOneWidget);
      expect(inEntry(0, 'Projector maintenance'), findsOneWidget);
      expect(inEntry(0, RoomScheduleSection.maintenance), findsOneWidget);
      expect(inEntry(1, '12:00–14:00'), findsOneWidget);
      expect(inEntry(1, RoomScheduleSection.free), findsOneWidget);
      expect(inEntry(2, '14:00–16:00'), findsOneWidget);
      expect(inEntry(2, RoomScheduleSection.booked), findsOneWidget);
      expect(inEntry(3, '16:00–20:00'), findsOneWidget);
      expect(inEntry(3, RoomScheduleSection.free), findsOneWidget);
      expect(entry(4), findsNothing);
      expect(find.text(RoomScheduleSection.fullyBooked), findsNothing);
      // A blackout looks different from a booking.
      Color colorOf(int i) => ((tester.widget<Container>(
            find.descendant(of: entry(i), matching: find.byType(Container)).first,
          ).decoration as BoxDecoration).color)!;
      expect(colorOf(0), isNot(colorOf(2)));
      expect(colorOf(1), isNot(colorOf(2)));
    });

    testWidgets('a closed day shows "Closed" and no free time', (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      await pumpRoomsScreens(tester, repository,
          initialLocation: '/rooms/3', now: _campusMorning(DateTime.utc(2026, 10, 4)));

      expect(find.text('Sun 4 Oct 2026'), findsOneWidget);
      expect(find.text(RoomScheduleSection.closed), findsOneWidget);
      expect(entry(0), findsNothing);
      expect(find.text(RoomScheduleSection.fullyBooked), findsNothing);
    });

    testWidgets('an open day with no free time shows "Fully booked"', (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      await pumpRoomsScreens(tester, repository,
          initialLocation: '/rooms/3', now: _campusMorning(DateTime.utc(2026, 10, 6)));

      expect(find.text(RoomScheduleSection.fullyBooked), findsOneWidget);
      expect(inEntry(0, '08:00–20:00'), findsOneWidget);
      expect(inEntry(0, RoomScheduleSection.booked), findsOneWidget);
    });

    testWidgets('next and previous request the right campus date; previous stops at today', (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/3');

      IconButton previous() => tester.widget<IconButton>(find.byKey(const Key('schedule.previous')));
      expect(previous().onPressed, isNull);

      await tester.tap(find.byKey(const Key('schedule.next')));
      await tester.pumpAndSettle();
      verify(() => repository.getSchedule(3, DateTime.utc(2026, 9, 29))).called(1);
      expect(find.text('Tue 29 Sep 2026'), findsOneWidget);
      expect(inEntry(0, '08:00–20:00'), findsOneWidget);

      await tester.tap(find.byKey(const Key('schedule.previous')));
      await tester.pumpAndSettle();
      expect(find.text('Mon 28 Sep 2026'), findsOneWidget);
      expect(inEntry(0, 'Projector maintenance'), findsOneWidget);
      expect(previous().onPressed, isNull);
    });

    testWidgets('the date picker requests the picked campus date', (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/3');

      await tester.tap(find.byKey(const Key('schedule.pick')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('30'));
      await tester.tap(find.text('OK'));
      await tester.pumpAndSettle();

      verify(() => repository.getSchedule(3, DateTime.utc(2026, 9, 30))).called(1);
      expect(find.text('Wed 30 Sep 2026'), findsOneWidget);
    });

    testWidgets('an error shows its title; Retry refetches the same date', (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      var calls = 0;
      when(() => repository.getSchedule(3, any())).thenAnswer((i) async {
        if (calls++ == 0) throw httpError('/api/rooms/3/schedule', 503, body: {'title': 'Service unavailable'});
        return _scheduleOn(i.positionalArguments[1] as DateTime);
      });
      await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/3');

      expect(find.text('Service unavailable'), findsOneWidget);
      expect(find.text('Room 3'), findsOneWidget);
      await tester.tap(find.widgetWithText(TextButton, 'Retry'));
      await tester.pumpAndSettle();

      expect(find.text('Service unavailable'), findsNothing);
      expect(inEntry(0, 'Projector maintenance'), findsOneWidget);
      verify(() => repository.getSchedule(3, DateTime.utc(2026, 9, 28))).called(2);
    });

    testWidgets('pull to refresh reloads the shown date', (tester) async {
      tallSurface(tester);
      when(() => repository.getRoom(3)).thenAnswer((_) async => testRoom(3));
      await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/3');
      await tester.tap(find.byKey(const Key('schedule.next')));
      await tester.pumpAndSettle();

      // The pull must cover a share of the (tall) viewport before the indicator arms.
      await tester.fling(find.text('Room 3'), const Offset(0, 1200), 1000);
      await tester.pumpAndSettle();

      verify(() => repository.getSchedule(3, DateTime.utc(2026, 9, 29))).called(2);
    });
  });

  testWidgets('403 shows access denied without Retry', (tester) async {
    when(() => repository.getRoom(5)).thenThrow(httpError('/api/rooms/5', 403, body: {'title': 'Forbidden'}));
    await pumpRoomsScreens(tester, repository, initialLocation: '/rooms/5');

    expect(find.text(ErrorRetryView.forbidden), findsOneWidget);
    expect(find.text('Retry'), findsNothing);
  });
}
