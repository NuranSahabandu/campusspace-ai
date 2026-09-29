import 'package:flutter/material.dart' show TimeOfDay;

import '../../core/campus_time.dart';
import '../auth/models.dart';

// The API's booking request DTOs (backend Dtos/Requests/BookingRequestDtos.cs) and the public policy values.
// Instants arrive as UTC ("...Z"); screens show them in campus time (core/campus_time.dart).

DateTime _instant(Object? value) => DateTime.parse(value as String).toUtc();

DateTime? _optionalInstant(Object? value) => value == null ? null : _instant(value);

double _money(Object? value) => (value as num).toDouble();

/// A club the caller can book for (ClubRefDto).
class ClubRef {
  const ClubRef({required this.id, required this.name});

  factory ClubRef.fromJson(Map<String, dynamic> json) =>
      ClubRef(id: (json['id'] as num).toInt(), name: json['name'] as String);

  final int id;
  final String name;
}

/// GET /api/booking-requests/eligibility. Reason explains why canSubmit is false.
class Eligibility {
  const Eligibility({
    required this.canSubmit,
    this.reason,
    this.clubs = const [],
    required this.openRequests,
    required this.maxOpenRequests,
    required this.clubRequired,
  });

  factory Eligibility.fromJson(Map<String, dynamic> json) => Eligibility(
        canSubmit: json['canSubmit'] as bool,
        reason: json['reason'] as String?,
        clubs: [for (final c in json['clubs'] as List) ClubRef.fromJson(c as Map<String, dynamic>)],
        openRequests: (json['openRequests'] as num).toInt(),
        maxOpenRequests: (json['maxOpenRequests'] as num).toInt(),
        clubRequired: json['clubRequired'] as bool,
      );

  final bool canSubmit;
  final String? reason;

  /// The active clubs the caller represents (always empty for a Lecturer).
  final List<ClubRef> clubs;
  final int openRequests;
  final int maxOpenRequests;

  /// True for Students (they book for a club); Lecturer bookings are academic.
  final bool clubRequired;
}

/// One day's opening hours in campus time.
class OpeningHours {
  const OpeningHours(this.open, this.close);

  final TimeOfDay open;
  final TimeOfDay close;
}

/// GET /api/policy-settings/public: the live booking policy (snake_case keys, typed values). The app never hard-codes
/// these numbers; the officer can change them at any time.
class PublicPolicy {
  const PublicPolicy({
    required this.openingHours,
    required this.minLeadTimeHours,
    required this.maxAdvanceDaysStudent,
    required this.maxAdvanceDaysLecturer,
    required this.maxDurationHours,
    required this.slotGranularityMinutes,
    required this.freeCancellationHours,
    required this.maxOpenRequests,
  });

  factory PublicPolicy.fromJson(Map<String, dynamic> json) {
    final hours = json['opening_hours'] as Map<String, dynamic>;
    return PublicPolicy(
      openingHours: {
        for (final (index, key) in _dayKeys.indexed) index + 1: _hours(hours[key]),
      },
      minLeadTimeHours: (json['min_lead_time_hours'] as num).toInt(),
      maxAdvanceDaysStudent: (json['max_advance_days_student'] as num).toInt(),
      maxAdvanceDaysLecturer: (json['max_advance_days_lecturer'] as num).toInt(),
      maxDurationHours: (json['max_duration_hours'] as num).toInt(),
      slotGranularityMinutes: (json['slot_granularity_minutes'] as num).toInt(),
      freeCancellationHours: (json['free_cancellation_hours'] as num).toInt(),
      maxOpenRequests: (json['max_open_requests'] as num).toInt(),
    );
  }

  // In DateTime.weekday order (Monday = 1).
  static const _dayKeys = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];

  static OpeningHours? _hours(Object? value) =>
      value is Map ? OpeningHours(_time(value['open']), _time(value['close'])) : null;

  static TimeOfDay _time(Object? value) {
    final [hour, minute] = (value as String).split(':');
    return TimeOfDay(hour: int.parse(hour), minute: int.parse(minute));
  }

  /// DateTime.weekday → hours; null means closed (for example Sunday).
  final Map<int, OpeningHours?> openingHours;
  final int minLeadTimeHours;
  final int maxAdvanceDaysStudent;
  final int maxAdvanceDaysLecturer;
  final int maxDurationHours;
  final int slotGranularityMinutes;

  /// An owner's cancellation of an approved booking later than this many hours before its start is flagged as late.
  final int freeCancellationHours;
  final int maxOpenRequests;

  OpeningHours? hoursOn(DateTime date) => openingHours[date.weekday];

  int maxAdvanceDays(String? role) => role == Roles.lecturer ? maxAdvanceDaysLecturer : maxAdvanceDaysStudent;
}

/// A row of GET /api/equipment-types. A type covered by a room feature is built into rooms that have it.
class EquipmentType {
  const EquipmentType({
    required this.id,
    required this.code,
    required this.name,
    required this.category,
    required this.feePerBooking,
    this.coveredByFeatureCode,
    this.coveredByFeatureName,
  });

  factory EquipmentType.fromJson(Map<String, dynamic> json) => EquipmentType(
        id: (json['id'] as num).toInt(),
        code: json['code'] as String,
        name: json['name'] as String,
        category: json['category'] as String,
        feePerBooking: _money(json['feePerBooking']),
        coveredByFeatureCode: json['coveredByFeatureCode'] as String?,
        coveredByFeatureName: json['coveredByFeatureName'] as String?,
      );

  final int id;
  final String code;
  final String name;
  final String category;
  final double feePerBooking;
  final String? coveredByFeatureCode;
  final String? coveredByFeatureName;
}

/// A request in My requests (BookingRequestSummaryDto).
class RequestSummary {
  const RequestSummary({
    required this.id,
    required this.purpose,
    required this.status,
    required this.requestedStart,
    required this.requestedEnd,
    required this.attendees,
    required this.budgetLkr,
    this.clubName,
    required this.requesterName,
    this.cancelledAt,
    this.isLateCancellation = false,
    this.cancelledByOfficer = false,
    required this.createdAt,
  });

  factory RequestSummary.fromJson(Map<String, dynamic> json) => RequestSummary(
        id: (json['id'] as num).toInt(),
        purpose: json['purpose'] as String,
        status: json['status'] as String,
        requestedStart: _instant(json['requestedStart']),
        requestedEnd: _instant(json['requestedEnd']),
        attendees: (json['attendees'] as num).toInt(),
        budgetLkr: _money(json['budgetLkr']),
        clubName: json['clubName'] as String?,
        requesterName: json['requesterName'] as String,
        cancelledAt: _optionalInstant(json['cancelledAt']),
        isLateCancellation: json['isLateCancellation'] as bool? ?? false,
        cancelledByOfficer: json['cancelledByOfficer'] as bool? ?? false,
        createdAt: _instant(json['createdAt']),
      );

  final int id;
  final String purpose;
  final String status;
  final DateTime requestedStart;
  final DateTime requestedEnd;
  final int attendees;
  final double budgetLkr;

  /// Null for an academic (Lecturer) booking.
  final String? clubName;
  final String requesterName;

  /// Set when the request was cancelled through the cancel operation.
  final DateTime? cancelledAt;

  /// An owner's late cancellation of an approved booking (flagged, not charged).
  final bool isLateCancellation;
  final bool cancelledByOfficer;
  final DateTime createdAt;
}

class RequesterRef {
  const RequesterRef({required this.id, required this.name, required this.email});

  factory RequesterRef.fromJson(Map<String, dynamic> json) => RequesterRef(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        email: json['email'] as String,
      );

  final int id;
  final String name;
  final String email;
}

class RequiredFeature {
  const RequiredFeature({required this.code, required this.name});

  factory RequiredFeature.fromJson(Map<String, dynamic> json) =>
      RequiredFeature(code: json['code'] as String, name: json['name'] as String);

  final String code;
  final String name;
}

class RequestedEquipment {
  const RequestedEquipment({required this.typeId, required this.typeCode, required this.typeName, required this.quantity});

  factory RequestedEquipment.fromJson(Map<String, dynamic> json) => RequestedEquipment(
        typeId: (json['typeId'] as num).toInt(),
        typeCode: json['typeCode'] as String,
        typeName: json['typeName'] as String,
        quantity: (json['quantity'] as num).toInt(),
      );

  final int typeId;
  final String typeCode;
  final String typeName;
  final int quantity;
}

/// One status change. changedById and changedByName are null when the system made the change.
class StatusChange {
  const StatusChange({
    this.fromStatus,
    required this.toStatus,
    this.changedById,
    this.changedByName,
    this.reason,
    required this.changedAt,
  });

  factory StatusChange.fromJson(Map<String, dynamic> json) => StatusChange(
        fromStatus: json['fromStatus'] as String?,
        toStatus: json['toStatus'] as String,
        changedById: (json['changedById'] as num?)?.toInt(),
        changedByName: json['changedByName'] as String?,
        reason: json['reason'] as String?,
        changedAt: _instant(json['changedAt']),
      );

  final String? fromStatus;
  final String toStatus;
  final int? changedById;
  final String? changedByName;
  final String? reason;
  final DateTime changedAt;
}

/// GET /api/booking-requests/{id} and the 202 body of POST (BookingRequestDetailDto). History is oldest first.
class RequestDetail {
  const RequestDetail({
    required this.id,
    required this.purpose,
    required this.status,
    required this.attendees,
    required this.requestedStart,
    required this.requestedEnd,
    required this.budgetLkr,
    this.notes,
    required this.requester,
    this.club,
    this.requiredFeatures = const [],
    this.equipment = const [],
    this.history = const [],
    this.cancelledAt,
    this.isLateCancellation = false,
    this.cancelledByOfficer = false,
    required this.createdAt,
  });

  factory RequestDetail.fromJson(Map<String, dynamic> json) => RequestDetail(
        id: (json['id'] as num).toInt(),
        purpose: json['purpose'] as String,
        status: json['status'] as String,
        attendees: (json['attendees'] as num).toInt(),
        requestedStart: _instant(json['requestedStart']),
        requestedEnd: _instant(json['requestedEnd']),
        budgetLkr: _money(json['budgetLkr']),
        notes: json['notes'] as String?,
        requester: RequesterRef.fromJson(json['requester'] as Map<String, dynamic>),
        club: json['club'] == null ? null : ClubRef.fromJson(json['club'] as Map<String, dynamic>),
        requiredFeatures: [
          for (final f in json['requiredFeatures'] as List) RequiredFeature.fromJson(f as Map<String, dynamic>),
        ],
        equipment: [
          for (final e in json['equipment'] as List) RequestedEquipment.fromJson(e as Map<String, dynamic>),
        ],
        history: [for (final h in json['history'] as List) StatusChange.fromJson(h as Map<String, dynamic>)],
        cancelledAt: _optionalInstant(json['cancelledAt']),
        isLateCancellation: json['isLateCancellation'] as bool? ?? false,
        cancelledByOfficer: json['cancelledByOfficer'] as bool? ?? false,
        createdAt: _instant(json['createdAt']),
      );

  final int id;
  final String purpose;
  final String status;
  final int attendees;
  final DateTime requestedStart;
  final DateTime requestedEnd;
  final double budgetLkr;
  final String? notes;
  final RequesterRef requester;
  final ClubRef? club;
  final List<RequiredFeature> requiredFeatures;
  final List<RequestedEquipment> equipment;
  final List<StatusChange> history;
  final DateTime? cancelledAt;
  final bool isLateCancellation;
  final bool cancelledByOfficer;
  final DateTime createdAt;
}

/// The POST /api/booking-requests body (CreateBookingRequestRequest).
class NewRequestBody {
  const NewRequestBody({
    required this.purpose,
    required this.attendees,
    required this.date,
    required this.start,
    required this.end,
    required this.budgetLkr,
    this.requiredFeatures = const [],
    this.equipment = const {},
    this.clubId,
    this.notes,
  });

  final String purpose;
  final int attendees;

  /// A campus date; [start] and [end] are campus times on it.
  final DateTime date;
  final TimeOfDay start;
  final TimeOfDay end;
  final num budgetLkr;
  final List<String> requiredFeatures;

  /// Type id → quantity. Zero means not requested and is not sent.
  final Map<int, int> equipment;

  /// Null for a Lecturer (academic booking).
  final int? clubId;
  final String? notes;

  Map<String, dynamic> toJson() => {
        'purpose': purpose.trim(),
        'attendees': attendees,
        'requestedStart': campusIso(date, start),
        'requestedEnd': campusIso(date, end),
        'budgetLkr': budgetLkr,
        'requiredFeatures': requiredFeatures,
        'equipment': [
          for (final MapEntry(key: typeId, value: quantity)
              in (equipment.entries.toList()..sort((a, b) => a.key.compareTo(b.key))))
            if (quantity > 0) {'typeId': typeId, 'quantity': quantity},
        ],
        'clubId': clubId,
        'notes': (notes == null || notes!.trim().isEmpty) ? null : notes,
      };
}
