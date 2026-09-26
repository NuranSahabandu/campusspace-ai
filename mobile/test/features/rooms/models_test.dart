import 'dart:convert';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/rooms_page.dart';

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
}
