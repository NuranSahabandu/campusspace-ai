import 'package:flutter/material.dart' show TimeOfDay;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

// Campus time is Asia/Colombo: UTC+05:30 all year (no DST). The phone's own time zone may be anything, so nothing
// here calls toLocal() or reads DateTime.now(). A campus date or wall-clock time is held in a DateTime.utc whose
// fields read as campus time ("campus wall clock"); an instant from the API is a real UTC DateTime.

const campusOffset = Duration(hours: 5, minutes: 30);
const _campusOffsetText = '+05:30';

/// The current instant. Widget tests override it to fix "now".
final clockProvider = Provider<DateTime Function()>((ref) => () => DateTime.now().toUtc());

/// The campus wall clock at [instant].
DateTime toCampus(DateTime instant) => instant.toUtc().add(campusOffset);

/// The campus date (midnight, wall clock) that contains [instant].
DateTime campusDateOf(DateTime instant) {
  final campus = toCampus(instant);
  return DateTime.utc(campus.year, campus.month, campus.day);
}

/// Today's campus date at [nowUtc].
DateTime campusToday(DateTime nowUtc) => campusDateOf(nowUtc);

/// A campus date from any DateTime's year, month and day (for example the value showDatePicker returns).
DateTime campusDate(DateTime date) => DateTime.utc(date.year, date.month, date.day);

/// The UTC instant of [time] on campus [date].
DateTime campusInstant(DateTime date, TimeOfDay time) =>
    DateTime.utc(date.year, date.month, date.day, time.hour, time.minute).subtract(campusOffset);

/// "2026-10-20T14:00:00+05:30": [time] on campus [date], as the API expects it.
String campusIso(DateTime date, TimeOfDay time) =>
    '${_pad(date.year, 4)}-${_pad(date.month)}-${_pad(date.day)}'
    'T${_pad(time.hour)}:${_pad(time.minute)}:00$_campusOffsetText';

/// Minutes since midnight.
int minutesOf(TimeOfDay time) => time.hour * 60 + time.minute;

String _pad(int value, [int width = 2]) => '$value'.padLeft(width, '0');

// Fixed patterns and locale: the text does not depend on the phone's language settings.
final _day = DateFormat('EEE d MMM y', 'en_US');
final _shortDay = DateFormat('EEE d MMM', 'en_US');
final _weekday = DateFormat('EEEE', 'en_US');
final _time = DateFormat('HH:mm', 'en_US');

/// "Tue 20 Oct 2026" for a campus date.
String formatCampusDay(DateTime campusDate) => _day.format(campusDate);

/// "Tuesday" for a campus date.
String formatWeekday(DateTime campusDate) => _weekday.format(campusDate);

/// "14:00".
String formatTimeOfDay(TimeOfDay time) => '${_pad(time.hour)}:${_pad(time.minute)}';

/// "Tue 20 Oct 2026" for the campus date of an API instant.
String formatCampusDate(DateTime instant) => formatCampusDay(toCampus(instant));

/// "Tue 20 Oct 2026, 14:05" for an API instant.
String formatCampusDateTime(DateTime instant) {
  final campus = toCampus(instant);
  return '${_day.format(campus)}, ${_time.format(campus)}';
}

/// "14:00–17:00" in campus time. When the end falls on a later campus date: "22:00–01:00 (Wed 21 Oct)".
String formatCampusTimeRange(DateTime startUtc, DateTime endUtc) {
  final start = toCampus(startUtc);
  final end = toCampus(endUtc);
  final range = '${_time.format(start)}–${_time.format(end)}';
  return campusDateOf(startUtc) == campusDateOf(endUtc) ? range : '$range (${_shortDay.format(end)})';
}

/// "Tue 20 Oct 2026 · 14:00–17:00".
String formatCampusSlot(DateTime startUtc, DateTime endUtc) =>
    '${formatCampusDate(startUtc)} · ${formatCampusTimeRange(startUtc, endUtc)}';
