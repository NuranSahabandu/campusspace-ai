import 'dart:convert';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:flutter/material.dart' show TimeOfDay;
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/rooms_page.dart';
import '../../fixtures/rooms_schedule.dart';

void main() {
  final json = jsonDecode(roomsPageJson) as Map<String, dynamic>;

  test('PagedResult<Room>.fromJson parses a real GET /api/rooms response', () {
    final result = PagedResult.fromJson(json, Room.fromJson);

    expect(result.page, 1);
    expect(result.pageSize, 20);
    expect(result.items, hasLength(result.total));
    expect(result.hasMore, isFalse);
  });

  test('Room.fromJson reads the building and the features', () {
    final items = (json['items'] as List).cast<Map<String, dynamic>>();
    final room = Room.fromJson(items.firstWhere((r) => r['code'] == 'A301'));

    expect(room.name, 'Computer Lab A301');
    expect(room.type, RoomTypes.computerLab);
    expect(room.capacity, 48);
    expect(room.isActive, isTrue);
    expect(room.building.code, 'MB');
    expect(room.building.name, 'Main Building');
    expect(room.features.map((f) => f.code), ['ac', 'computers', 'projector', 'whiteboard']);
  });

  test('hasMore counts the items on earlier pages', () {
    PagedResult<int> page(int number, int count, int total) =>
        PagedResult(items: List.filled(count, 0), page: number, pageSize: 20, total: total);

    expect(page(1, 20, 25).hasMore, isTrue);
    expect(page(2, 5, 25).hasMore, isFalse);
    expect(page(1, 0, 0).hasMore, isFalse);
  });

  test('RoomTypes.label', () {
    expect(RoomTypes.label(RoomTypes.lectureHall), 'Lecture hall');
    expect(RoomTypes.label(RoomTypes.auditorium), 'Auditorium');
    expect(RoomTypes.label('Unknown'), 'Unknown');
  });

  group('RoomSchedule.fromJson (real GET /api/rooms/{id}/schedule responses)', () {
    RoomSchedule parse(String text) => RoomSchedule.fromJson(jsonDecode(text) as Map<String, dynamic>);

    test('reads the date, the hours and UTC busy and free intervals', () {
      final schedule = parse(scheduleBusyJson);

      expect(schedule.date, DateTime.utc(2026, 9, 28));
      expect(schedule.open, const TimeOfDay(hour: 8, minute: 0));
      expect(schedule.close, const TimeOfDay(hour: 20, minute: 0));
      expect(schedule.granularityMinutes, 30);
      expect(schedule.isClosed, isFalse);
      expect(schedule.isFullyBooked, isFalse);
      expect(schedule.busy.first.kind, ScheduleKinds.blackout);
      expect(schedule.busy.first.isBlackout, isTrue);
      expect(schedule.busy.first.label, 'Projector maintenance');
      expect(schedule.busy.first.start, DateTime.utc(2026, 9, 28, 2, 30));
      expect(schedule.busy.first.start.isUtc, isTrue);
      expect(schedule.busy.last.label, 'Booked');
      expect(schedule.free, hasLength(2));
    });

    test('entries merges busy and free rows by start time', () {
      final entries = parse(scheduleBusyJson).entries;

      expect(entries.map((e) => (e.runtimeType, e.start.hour, e.start.minute)), [
        (BusyInterval, 2, 30),
        (FreeInterval, 6, 30),
        (BusyInterval, 8, 30),
        (FreeInterval, 10, 30),
      ]);
    });

    test('a busy row sorts before a free row with the same start', () {
      final at = DateTime.utc(2026, 9, 28, 4);
      final schedule = RoomSchedule(
        date: DateTime.utc(2026, 9, 28),
        granularityMinutes: 30,
        free: [FreeInterval(start: at, end: at.add(const Duration(hours: 1)))],
        busy: [BusyInterval(start: at, end: at.add(const Duration(hours: 1)), kind: ScheduleKinds.booking, label: 'Booked')],
      );

      expect(schedule.entries.first, isA<BusyInterval>());
    });

    test('a closed day has no hours and is not "fully booked"', () {
      final schedule = parse(scheduleClosedJson);

      expect(schedule.open, isNull);
      expect(schedule.isClosed, isTrue);
      expect(schedule.isFullyBooked, isFalse);
      expect(schedule.entries, isEmpty);
    });

    test('an open day with no free interval is fully booked', () {
      expect(parse(scheduleFullJson).isFullyBooked, isTrue);
    });
  });
}
