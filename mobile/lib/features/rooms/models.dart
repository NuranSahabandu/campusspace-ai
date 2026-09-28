import 'package:flutter/material.dart' show TimeOfDay;

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

/// The kinds of busy interval in a day schedule, mirroring backend ScheduleKinds.
abstract final class ScheduleKinds {
  static const booking = 'Booking';
  static const blackout = 'Blackout';
}

DateTime _instant(Object? value) => DateTime.parse(value as String).toUtc();

TimeOfDay? _optionalTime(Object? value) {
  if (value == null) return null;
  final [hour, minute] = (value as String).split(':');
  return TimeOfDay(hour: int.parse(hour), minute: int.parse(minute));
}

/// One row of a day schedule: [start, end) in UTC. Busy rows have a kind and a label; free rows have neither.
sealed class ScheduleEntry {
  const ScheduleEntry({required this.start, required this.end});

  final DateTime start;
  final DateTime end;
}

/// BusyIntervalDto. Label is "Booked" for a booking (never who or why) or the blackout's reason.
class BusyInterval extends ScheduleEntry {
  const BusyInterval({required super.start, required super.end, required this.kind, required this.label});

  factory BusyInterval.fromJson(Map<String, dynamic> json) => BusyInterval(
        start: _instant(json['start']),
        end: _instant(json['end']),
        kind: json['kind'] as String,
        label: json['label'] as String,
      );

  final String kind;
  final String label;

  bool get isBlackout => kind == ScheduleKinds.blackout;
}

/// FreeIntervalDto: open time with no booking or blackout, on slot boundaries.
class FreeInterval extends ScheduleEntry {
  const FreeInterval({required super.start, required super.end});

  factory FreeInterval.fromJson(Map<String, dynamic> json) =>
      FreeInterval(start: _instant(json['start']), end: _instant(json['end']));
}

/// GET /api/rooms/{id}/schedule?date=yyyy-MM-dd (UC03): one campus day. Open and close are null on a closed day,
/// which can still have busy intervals (for example a blackout).
class RoomSchedule {
  const RoomSchedule({
    required this.date,
    this.open,
    this.close,
    required this.granularityMinutes,
    this.busy = const [],
    this.free = const [],
  });

  factory RoomSchedule.fromJson(Map<String, dynamic> json) {
    final [year, month, day] = (json['date'] as String).split('-').map(int.parse).toList();
    return RoomSchedule(
      date: DateTime.utc(year, month, day),
      open: _optionalTime(json['open']),
      close: _optionalTime(json['close']),
      granularityMinutes: (json['granularityMinutes'] as num).toInt(),
      busy: [for (final b in json['busy'] as List) BusyInterval.fromJson(b as Map<String, dynamic>)],
      free: [for (final f in json['free'] as List) FreeInterval.fromJson(f as Map<String, dynamic>)],
    );
  }

  /// The campus date.
  final DateTime date;
  final TimeOfDay? open;
  final TimeOfDay? close;
  final int granularityMinutes;
  final List<BusyInterval> busy;
  final List<FreeInterval> free;

  bool get isClosed => open == null || close == null;

  /// Open but with no free slot left.
  bool get isFullyBooked => !isClosed && free.isEmpty;

  /// Busy and free rows in one list by start time; a busy row comes before a free row that starts with it.
  /// (List.sort is not stable, so ties are broken by end and kind as the server orders them.)
  List<ScheduleEntry> get entries => [...busy, ...free]..sort((a, b) {
      int rank(ScheduleEntry e) => e is FreeInterval ? 1 : 0;
      final byStart = a.start.compareTo(b.start);
      if (byStart != 0) return byStart;
      final byRank = rank(a) - rank(b);
      if (byRank != 0) return byRank;
      final byEnd = a.end.compareTo(b.end);
      if (byEnd != 0 || a is! BusyInterval || b is! BusyInterval) return byEnd;
      return a.kind.compareTo(b.kind);
    });
}
