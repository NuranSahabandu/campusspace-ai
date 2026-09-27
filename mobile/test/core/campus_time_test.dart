import 'package:campusspace_mobile/core/campus_time.dart';
import 'package:campusspace_mobile/core/format.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

// Every expectation here is fixed text for a fixed instant, so the suite passes in any device time zone.
// Checked locally with TZ=America/New_York, Asia/Tokyo, UTC and Pacific/Kiritimati.
void main() {
  group('campusIso', () {
    test('builds the API string with +05:30 from a date and a time', () {
      expect(campusIso(DateTime.utc(2026, 10, 20), const TimeOfDay(hour: 14, minute: 0)), '2026-10-20T14:00:00+05:30');
      expect(campusIso(DateTime.utc(2026, 1, 5), const TimeOfDay(hour: 8, minute: 30)), '2026-01-05T08:30:00+05:30');
    });

    test('uses only the year, month and day of a local date from the date picker', () {
      // showDatePicker returns local midnight; in a zone west of UTC, toUtc() would move it to the day before.
      expect(campusIso(DateTime(2026, 10, 20), const TimeOfDay(hour: 14, minute: 0)), '2026-10-20T14:00:00+05:30');
    });

    test('names the same instant as campusInstant', () {
      final iso = campusIso(DateTime.utc(2026, 10, 20), const TimeOfDay(hour: 14, minute: 0));
      expect(DateTime.parse(iso), campusInstant(DateTime.utc(2026, 10, 20), const TimeOfDay(hour: 14, minute: 0)));
      expect(campusInstant(DateTime.utc(2026, 10, 20), const TimeOfDay(hour: 14, minute: 0)),
          DateTime.utc(2026, 10, 20, 8, 30));
    });
  });

  group('UTC → campus', () {
    test('an API instant shows in campus time', () {
      final start = DateTime.parse('2026-10-20T08:30:00Z');
      final end = DateTime.parse('2026-10-20T11:30:00Z');
      expect(formatCampusDate(start), 'Tue 20 Oct 2026');
      expect(formatCampusTimeRange(start, end), '14:00–17:00');
      expect(formatCampusSlot(start, end), 'Tue 20 Oct 2026 · 14:00–17:00');
      expect(formatCampusDateTime(DateTime.parse('2026-09-27T13:02:47.677Z')), 'Sun 27 Sep 2026, 18:32');
    });

    test('an instant late in the UTC day is the next campus day', () {
      // 20:00Z is 01:30 on the 21st in Colombo.
      final instant = DateTime.utc(2026, 10, 20, 20, 0);
      expect(campusDateOf(instant), DateTime.utc(2026, 10, 21));
      expect(formatCampusDate(instant), 'Wed 21 Oct 2026');
      expect(campusToday(instant), DateTime.utc(2026, 10, 21));
    });

    test('a range that crosses campus midnight names the end day', () {
      final start = DateTime.utc(2026, 10, 20, 16, 30); // 22:00 campus
      final end = DateTime.utc(2026, 10, 20, 19, 30); // 01:00 next day campus
      expect(formatCampusTimeRange(start, end), '22:00–01:00 (Wed 21 Oct)');
    });

    test('a local DateTime for the same instant gives the same campus text', () {
      final utc = DateTime.utc(2026, 10, 20, 8, 30);
      expect(formatCampusDateTime(utc.toLocal()), formatCampusDateTime(utc));
      expect(toCampus(utc.toLocal()), toCampus(utc));
    });
  });

  test('formatTimeOfDay pads to HH:mm', () {
    expect(formatTimeOfDay(const TimeOfDay(hour: 8, minute: 5)), '08:05');
  });

  test('formatLkr matches the web', () {
    expect(formatLkr(8000), 'LKR 8,000.00');
    expect(formatLkr(1500.5), 'LKR 1,500.50');
    expect(formatLkr(0), 'LKR 0.00');
  });
}
