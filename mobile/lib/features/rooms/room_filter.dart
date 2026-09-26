import 'package:flutter/foundation.dart';

/// The Browse rooms filters, as sent to GET /api/rooms (backend Dtos/Facilities/RoomsQuery.cs).
@immutable
class RoomFilter {
  const RoomFilter({this.search = '', this.buildingId, this.type, this.minCapacity = 0, this.features = const {}});

  final String search;
  final int? buildingId;
  final String? type;

  /// 0 means no minimum (the API requires minCapacity ≥ 1, so 0 is never sent).
  final int minCapacity;

  /// Feature codes; a room must have all of them.
  final Set<String> features;

  bool get isEmpty =>
      search.trim().isEmpty && buildingId == null && type == null && minCapacity <= 0 && features.isEmpty;

  /// Nullable fields take a function so they can be reset: `copyWith(buildingId: () => null)`.
  RoomFilter copyWith({
    String? search,
    int? Function()? buildingId,
    String? Function()? type,
    int? minCapacity,
    Set<String>? features,
  }) =>
      RoomFilter(
        search: search ?? this.search,
        buildingId: buildingId != null ? buildingId() : this.buildingId,
        type: type != null ? type() : this.type,
        minCapacity: minCapacity ?? this.minCapacity,
        features: features ?? this.features,
      );

  /// Query parameters without the empty values. Features are sorted and comma-separated.
  Map<String, String> toQuery() => {
        if (search.trim().isNotEmpty) 'search': search.trim(),
        if (buildingId != null) 'buildingId': '$buildingId',
        'type': ?type,
        if (minCapacity > 0) 'minCapacity': '$minCapacity',
        if (features.isNotEmpty) 'features': (features.toList()..sort()).join(','),
      };

  @override
  bool operator ==(Object other) =>
      other is RoomFilter &&
      other.search == search &&
      other.buildingId == buildingId &&
      other.type == type &&
      other.minCapacity == minCapacity &&
      setEquals(other.features, features);

  @override
  int get hashCode => Object.hash(search, buildingId, type, minCapacity, Object.hashAllUnordered(features));

  @override
  String toString() => 'RoomFilter(${toQuery()})';
}
