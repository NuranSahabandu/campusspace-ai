import 'dart:convert';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/core/api/problem.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/requests.dart';

Map<String, dynamic> json(String text) => jsonDecode(text) as Map<String, dynamic>;

void main() {
  test('eligibility parses for a student, a non-representative and a lecturer', () {
    final student = Eligibility.fromJson(json(eligibilityStudentJson));
    expect(student.canSubmit, isTrue);
    expect(student.clubRequired, isTrue);
    expect(student.clubs.single.name, 'Robotics Club');
    expect(student.maxOpenRequests, 3);

    final notRep = Eligibility.fromJson(json(eligibilityNotRepJson));
    expect(notRep.canSubmit, isFalse);
    expect(notRep.reason, 'You must be the registered representative of an active club');
    expect(notRep.clubs, isEmpty);

    final lecturer = Eligibility.fromJson(json(eligibilityLecturerJson));
    expect(lecturer.clubRequired, isFalse);
    expect(lecturer.clubs, isEmpty);
  });

  test('policy parses opening hours by weekday (Sunday closed) and the numbers', () {
    final policy = PublicPolicy.fromJson(json(policyJson));
    expect(policy.openingHours[DateTime.monday]!.open, const TimeOfDay(hour: 8, minute: 0));
    expect(policy.openingHours[DateTime.saturday]!.close, const TimeOfDay(hour: 16, minute: 0));
    expect(policy.openingHours[DateTime.sunday], isNull);
    expect(policy.minLeadTimeHours, 48);
    expect(policy.maxAdvanceDays('Student'), 60);
    expect(policy.maxAdvanceDays('Lecturer'), 90);
    expect(policy.maxDurationHours, 8);
    expect(policy.slotGranularityMinutes, 30);
  });

  test('equipment types parse, including the covering feature', () {
    final types = PagedResult.fromJson(json(equipmentTypesJson), EquipmentType.fromJson).items;
    expect(types, hasLength(10));
    final projector = types.singleWhere((t) => t.code == 'PROJ-PORTABLE');
    expect(projector.feePerBooking, 1500);
    expect(projector.coveredByFeatureName, 'Projector');
    expect(types.singleWhere((t) => t.code == 'MIC-WIRELESS').coveredByFeatureCode, isNull);
  });

  test('a list page parses with UTC times', () {
    final page = PagedResult.fromJson(json(requestsPageJson), RequestSummary.fromJson);
    final first = page.items.first;
    expect(first.requestedStart.isUtc, isTrue);
    expect(first.status, 'Submitted');
    expect(first.clubName, isNull);
  });

  test('a detail parses with its history and the changer id', () {
    final detail = RequestDetail.fromJson(json(requestDetailJson));
    expect(detail.requester.name, 'Dr. Nimal Fernando');
    expect(detail.club, isNull);
    expect(detail.requiredFeatures.map((f) => f.name), ['Projector', 'Sound system']);
    expect(detail.equipment.single.typeName, 'Wireless microphone');
    expect(detail.history.single.changedById, detail.requester.id);
    expect(detail.history.single.fromStatus, isNull);
    expect(detail.budgetLkr, 0);
  });

  test('the captured 400 maps to camelCase field errors', () {
    final problem = Problem.fromJson(json(submit400Json), status: 400);
    expect(problem.fieldErrors.keys, containsAll(['requestedStart', 'budgetLkr', 'clubId']));
  });

  group('NewRequestBody.toJson', () {
    NewRequestBody body({int? clubId, Map<int, int> equipment = const {}, String? notes}) => NewRequestBody(
          purpose: '  Robotics Club workshop ',
          attendees: 45,
          date: DateTime.utc(2026, 10, 20),
          start: const TimeOfDay(hour: 14, minute: 0),
          end: const TimeOfDay(hour: 17, minute: 0),
          budgetLkr: 8000,
          requiredFeatures: const ['computers', 'projector'],
          equipment: equipment,
          clubId: clubId,
          notes: notes,
        );

    test('sends +05:30 times, only lines with a quantity, and the club', () {
      expect(body(clubId: 1, equipment: {5: 0, 1: 2}, notes: 'prefer near the main building').toJson(), {
        'purpose': 'Robotics Club workshop',
        'attendees': 45,
        'requestedStart': '2026-10-20T14:00:00+05:30',
        'requestedEnd': '2026-10-20T17:00:00+05:30',
        'budgetLkr': 8000,
        'requiredFeatures': ['computers', 'projector'],
        'equipment': [
          {'typeId': 1, 'quantity': 2},
        ],
        'clubId': 1,
        'notes': 'prefer near the main building',
      });
    });

    test('blank notes and no club are sent as null', () {
      final sent = body(notes: '   ').toJson();
      expect(sent['clubId'], isNull);
      expect(sent.containsKey('clubId'), isTrue);
      expect(sent['notes'], isNull);
      expect(sent['equipment'], isEmpty);
    });
  });
}
