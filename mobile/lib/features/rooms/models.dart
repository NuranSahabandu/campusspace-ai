/// Room types, mirroring backend Models/RoomTypes.cs. Keep the two in sync.
abstract final class RoomTypes {
  static const lectureHall = 'LectureHall';
  static const computerLab = 'ComputerLab';
  static const seminarRoom = 'SeminarRoom';
  static const auditorium = 'Auditorium';

  static const all = [lectureHall, computerLab, seminarRoom, auditorium];

  /// Display label; unknown values are shown as they are.
  static String label(String type) => switch (type) {
        lectureHall => 'Lecture hall',
        computerLab => 'Computer lab',
        seminarRoom => 'Seminar room',
        _ => type,
      };
}

/// The API's BuildingRefDto: the building a room belongs to.
class BuildingRef {
  const BuildingRef({required this.id, required this.code, required this.name});

  factory BuildingRef.fromJson(Map<String, dynamic> json) => BuildingRef(
        id: (json['id'] as num).toInt(),
        code: json['code'] as String,
        name: json['name'] as String,
      );

  final int id;
  final String code;
  final String name;
}

/// The API's FeatureRefDto (it has no id: features are identified by code).
class FeatureRef {
  const FeatureRef({required this.code, required this.name});

  factory FeatureRef.fromJson(Map<String, dynamic> json) =>
      FeatureRef(code: json['code'] as String, name: json['name'] as String);

  final String code;
  final String name;
}

/// The API's RoomDto. Features are ordered by code.
class Room {
  const Room({
    required this.id,
    required this.code,
    required this.name,
    required this.type,
    required this.capacity,
    required this.isActive,
    required this.building,
    required this.features,
  });

  factory Room.fromJson(Map<String, dynamic> json) => Room(
        id: (json['id'] as num).toInt(),
        code: json['code'] as String,
        name: json['name'] as String,
        type: json['type'] as String,
        capacity: (json['capacity'] as num).toInt(),
        isActive: json['isActive'] as bool,
        building: BuildingRef.fromJson(json['building'] as Map<String, dynamic>),
        features: [for (final f in json['features'] as List) FeatureRef.fromJson(f as Map<String, dynamic>)],
      );

  final int id;
  final String code;
  final String name;
  final String type;
  final int capacity;
  final bool isActive;
  final BuildingRef building;
  final List<FeatureRef> features;
}

/// The API's BuildingDto. Non-officers only receive active buildings.
class Building {
  const Building({required this.id, required this.code, required this.name, required this.isActive});

  factory Building.fromJson(Map<String, dynamic> json) => Building(
        id: (json['id'] as num).toInt(),
        code: json['code'] as String,
        name: json['name'] as String,
        isActive: json['isActive'] as bool,
      );

  final int id;
  final String code;
  final String name;
  final bool isActive;
}

/// The API's FeatureDto. The code is what filters (and the agents) use.
class Feature {
  const Feature({required this.id, required this.code, required this.name});

  factory Feature.fromJson(Map<String, dynamic> json) => Feature(
        id: (json['id'] as num).toInt(),
        code: json['code'] as String,
        name: json['name'] as String,
      );

  final int id;
  final String code;
  final String name;
}
