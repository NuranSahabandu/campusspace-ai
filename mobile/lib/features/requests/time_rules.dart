import 'package:flutter/material.dart' show TimeOfDay;

import '../../core/campus_time.dart';
import 'models.dart';

// Client-side date and time rules for the New request form. The server enforces lead time, opening hours,
// granularity, duration and the advance window from Phase 2 (V05/V06); until then these checks are the only guide.
// Both sides read the same live policy (GET /api/policy-settings/public), never hard-coded numbers.
// Pure functions: "now" is always passed in, and dates are campus dates (see core/campus_time.dart).

/// The earliest date the picker offers: the campus date of now + min_lead_time_hours. Times on that date that are
/// still too soon are rejected by [validateTimes].
DateTime firstBookableDate(DateTime nowUtc, PublicPolicy policy) =>
    campusDateOf(nowUtc.add(Duration(hours: policy.minLeadTimeHours)));

/// The latest date: campus today + the role's advance window.
DateTime lastBookableDate(DateTime nowUtc, PublicPolicy policy, String? role) =>
    campusToday(nowUtc).add(Duration(days: policy.maxAdvanceDays(role)));

/// The date picker's selectableDayPredicate: a day with null opening hours (for example Sunday) is closed.
bool isOpenDay(DateTime date, PublicPolicy policy) => policy.hoursOn(date) != null;

/// The first open day in [first, last], or null when there is none.
DateTime? firstOpenDay(DateTime first, DateTime last, PublicPolicy policy) {
  for (var day = campusDate(first); !day.isAfter(last); day = day.add(const Duration(days: 1))) {
    if (isOpenDay(day, policy)) return day;
  }
  return null;
}

/// Inline messages for the start and end fields; null means valid.
typedef TimeErrors = ({String? start, String? end});

/// Checks [start]–[end] on campus [date]: both on the slot granularity, within that day's opening hours, end after
/// start, at most max_duration_hours, and a start at least min_lead_time_hours after [nowUtc].
TimeErrors validateTimes({
  required DateTime date,
  required TimeOfDay? start,
  required TimeOfDay? end,
  required PublicPolicy policy,
  required DateTime nowUtc,
}) {
  final hours = policy.hoursOn(date);
  final granularity = policy.slotGranularityMinutes;
  final onBoundary = 'Must be on a $granularity-minute boundary';
  final days = '${formatWeekday(date)}s';

  String? startError;
  if (start == null) {
    startError = 'Pick a start time';
  } else if (hours == null) {
    startError = 'The campus is closed on $days';
  } else if (minutesOf(start) % granularity != 0) {
    startError = onBoundary;
  } else if (minutesOf(start) < minutesOf(hours.open)) {
    startError = 'Opens at ${formatTimeOfDay(hours.open)} on $days';
  } else if (minutesOf(start) >= minutesOf(hours.close)) {
    startError = 'Closes at ${formatTimeOfDay(hours.close)} on $days';
  } else if (campusInstant(date, start).isBefore(nowUtc.add(Duration(hours: policy.minLeadTimeHours)))) {
    startError = 'Must start at least ${policy.minLeadTimeHours} hours from now';
  }

  String? endError;
  if (end == null) {
    endError = 'Pick an end time';
  } else if (minutesOf(end) % granularity != 0) {
    endError = onBoundary;
  } else if (start != null && minutesOf(end) <= minutesOf(start)) {
    endError = 'End must be after start';
  } else if (hours != null && minutesOf(end) > minutesOf(hours.close)) {
    endError = 'Must end by ${formatTimeOfDay(hours.close)} on $days';
  } else if (start != null && minutesOf(end) - minutesOf(start) > policy.maxDurationHours * 60) {
    endError = 'Bookings can be at most ${policy.maxDurationHours} hours';
  }

  return (start: startError, end: endError);
}
