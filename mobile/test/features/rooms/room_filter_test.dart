import 'package:campusspace_mobile/features/rooms/room_filter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('RoomFilter.toQuery', () {
    test('features are sent as one comma-separated value', () {
      const filter = RoomFilter(features: {'projector', 'computers'});

      expect(filter.toQuery(), {'features': 'computers,projector'});
    });

    test('empty values are omitted', () {
      expect(const RoomFilter().toQuery(), isEmpty);
      expect(const RoomFilter(search: '   ').toQuery(), isEmpty);
    });

    test('minCapacity 0 is omitted (the API requires at least 1)', () {
      expect(const RoomFilter(minCapacity: 0).toQuery(), isEmpty);
      expect(const RoomFilter(minCapacity: 45).toQuery(), {'minCapacity': '45'});
    });

    test('every field', () {
      const filter = RoomFilter(
          search: ' lab ', buildingId: 2, type: 'ComputerLab', minCapacity: 45, features: {'computers'});

      expect(filter.toQuery(), {
        'search': 'lab',
        'buildingId': '2',
        'type': 'ComputerLab',
        'minCapacity': '45',
        'features': 'computers',
      });
    });
  });

  test('copyWith keeps, sets and clears nullable fields', () {
    const filter = RoomFilter(buildingId: 1, type: 'Auditorium');

    expect(filter.copyWith(minCapacity: 5).buildingId, 1);
    expect(filter.copyWith(buildingId: () => 3).buildingId, 3);
    expect(filter.copyWith(buildingId: () => null).buildingId, isNull);
    expect(filter.copyWith(type: () => null).type, isNull);
  });

  test('equality ignores feature order; isEmpty', () {
    expect(const RoomFilter(features: {'a', 'b'}), const RoomFilter(features: {'b', 'a'}));
    expect(const RoomFilter(features: {'a', 'b'}).hashCode, const RoomFilter(features: {'b', 'a'}).hashCode);
    expect(const RoomFilter(minCapacity: 5), isNot(const RoomFilter()));
    expect(const RoomFilter().isEmpty, isTrue);
    expect(const RoomFilter(features: {'a'}).isEmpty, isFalse);
  });
}
