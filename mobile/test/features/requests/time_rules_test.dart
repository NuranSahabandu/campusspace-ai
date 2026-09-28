import 'dart:convert';

import 'package:campusspace_mobile/core/campus_time.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:campusspace_mobile/features/requests/time_rules.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/requests.dart';

TimeOfDay t(int hour, [int minute = 0]) => TimeOfDay(hour: hour, minute: minute);

void main() {
  // The live policy as captured: 48 h lead, 60/90 days, 8 h, 30 min, Sat 08:00–16:00, Sunday closed.
  final policy = PublicPolicy.fromJson(jsonDecode(policyJson) as Map<String, dynamic>);

  // Sunday 18 Oct 2026, 18:41 campus time.
  final sundayEvening = campusInstant(DateTime.utc(2026, 10, 18), t(18, 41));
  final tuesday = DateTime.utc(2026, 10, 20);
  final saturday = DateTime.utc(2026, 10, 24);
  final sunday = DateTime.utc(2026, 10, 25);
  // Far enough ahead that the lead time never interferes.
  final early = DateTime.utc(2026, 10, 1);

  TimeErrors check(DateTime date, TimeOfDay? start, TimeOfDay? end, {DateTime? now}) =>
      validateTimes(date: date, start: start, end: end, policy: policy, nowUtc: now ?? early);

  group('bookable dates', () {
    test('first date is the campus date of now + the lead time, not rounded up', () {
      expect(firstBookableDate(sundayEvening, policy), tuesday);
    });

    test('last date depends on the role', () {
      expect(lastBookableDate(sundayEvening, policy, Roles.student), DateTime.utc(2026, 12, 17));
      expect(lastBookableDate(sundayEvening, policy, Roles.lecturer), DateTime.utc(2027, 1, 16));
    });

    test('Sunday (null opening hours) is not selectable; Saturday is', () {
      expect(isOpenDay(sunday, policy), isFalse);
      expect(isOpenDay(saturday, policy), isTrue);
      expect(isOpenDay(tuesday, policy), isTrue);
    });

    test('firstOpenDay skips closed days', () {
      expect(firstOpenDay(sunday, DateTime.utc(2026, 10, 30), policy), DateTime.utc(2026, 10, 26));
      expect(firstOpenDay(sunday, sunday, policy), isNull);
    });
  });

  group('validateTimes', () {
    test('a valid weekday slot has no errors', () {
      expect(check(tuesday, t(14), t(17)), (start: null, end: null));
    });

    test('both times must be picked', () {
      expect(check(tuesday, null, null), (start: 'Pick a start time', end: 'Pick an end time'));
    });

    test('times must be on the slot granularity (14:15 is rejected)', () {
      expect(check(tuesday, t(14, 15), t(17)).start, 'Must be on a 30-minute boundary');
      expect(check(tuesday, t(14), t(17, 10)).end, 'Must be on a 30-minute boundary');
      expect(check(tuesday, t(14, 30), t(17, 30)), (start: null, end: null));
    });

    test('end must be after start', () {
      expect(check(tuesday, t(14), t(14)).end, 'End must be after start');
      expect(check(tuesday, t(14), t(13)).end, 'End must be after start');
    });

    test('within opening hours; Saturday closes at 16:00', () {
      expect(check(tuesday, t(7, 30), t(9)).start, 'Opens at 08:00 on Tuesdays');
      expect(check(tuesday, t(19), t(20, 30)).end, 'Must end by 20:00 on Tuesdays');
      expect(check(saturday, t(14), t(16)), (start: null, end: null));
      expect(check(saturday, t(14), t(17)).end, 'Must end by 16:00 on Saturdays');
      expect(check(saturday, t(16), t(17)).start, 'Closes at 16:00 on Saturdays');
      expect(check(sunday, t(10), t(12)).start, 'The campus is closed on Sundays');
    });

    test('duration is at most max_duration_hours', () {
      expect(check(tuesday, t(8), t(16)), (start: null, end: null));
      expect(check(tuesday, t(8), t(16, 30)).end, 'Bookings can be at most 8 hours');
    });

    test('the start must be at least min_lead_time_hours from now', () {
      // Now is Sunday 18:41, so Tuesday 18:41 is the earliest start.
      expect(check(tuesday, t(18, 30), t(19, 30), now: sundayEvening).start, 'Must start at least 48 hours from now');
      expect(check(tuesday, t(19), t(20), now: sundayEvening), (start: null, end: null));
    });
  });

  group('isLateCancellation (free_cancellation_hours = 24)', () {
    // Mon 26 Oct 2026, 10:00 campus time.
    final start = campusInstant(DateTime.utc(2026, 10, 26), t(10));
    final boundary = start.subtract(const Duration(hours: 24));

    bool late(String status, DateTime now) =>
        isLateCancellation(status: status, startUtc: start, nowUtc: now, policy: policy);

    test('an approved request is late only after start − 24 h; the boundary itself is free', () {
      expect(late(RequestStatuses.approved, boundary.subtract(const Duration(minutes: 1))), isFalse);
      expect(late(RequestStatuses.approved, boundary), isFalse);
      expect(late(RequestStatuses.approved, boundary.add(const Duration(minutes: 1))), isTrue);
    });

    test('a request that is not approved is never late', () {
      final inside = boundary.add(const Duration(hours: 1));
      expect(late(RequestStatuses.submitted, inside), isFalse);
      expect(late(RequestStatuses.pendingApproval, inside), isFalse);
    });

    test('reads the hours from the policy, never a fixed 24', () {
      final strict = PublicPolicy.fromJson(
          {...jsonDecode(policyJson) as Map<String, dynamic>, 'free_cancellation_hours': 48});
      final now = start.subtract(const Duration(hours: 30));
      expect(late(RequestStatuses.approved, now), isFalse);
      expect(isLateCancellation(status: RequestStatuses.approved, startUtc: start, nowUtc: now, policy: strict), isTrue);
    });
  });

  test('RequestStatuses.cancellable matches the backend state machine', () {
    expect(RequestStatuses.cancellable, {'Submitted', 'PendingApproval', 'Approved'});
  });
}
