import 'package:flutter/material.dart' show TimeOfDay;
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/validators.dart';
import 'models.dart';
import 'requests_providers.dart';
import 'requests_repository.dart';
import 'time_rules.dart';

/// The New request form's field names: the API's camelCase error keys, plus `requestedDate` for the date picker.
abstract final class RequestFields {
  static const purpose = 'purpose';
  static const clubId = 'clubId';
  static const requestedDate = 'requestedDate';
  static const requestedStart = 'requestedStart';
  static const requestedEnd = 'requestedEnd';
  static const attendees = 'attendees';
  static const requiredFeatures = 'requiredFeatures';
  static const equipment = 'equipment';
  static const budgetLkr = 'budgetLkr';
  static const notes = 'notes';
}

/// The stepper's steps, in order.
abstract final class RequestSteps {
  static const purpose = 0;
  static const when = 1;
  static const needs = 2;
  static const budget = 3;
  static const review = 4;
  static const count = 5;

  /// The step that shows [field]. Model-binding keys such as `equipment[0].Quantity` go with their list; anything
  /// unknown is shown on the Review step.
  static int of(String field) => switch (field) {
        RequestFields.purpose || RequestFields.clubId => purpose,
        RequestFields.requestedDate ||
        RequestFields.requestedStart ||
        RequestFields.requestedEnd ||
        RequestFields.attendees =>
          when,
        _ when field.startsWith(RequestFields.requiredFeatures) || field.startsWith(RequestFields.equipment) => needs,
        RequestFields.budgetLkr || RequestFields.notes => budget,
        _ => review,
      };

  /// The first step that shows one of [fields], or null when there are none.
  static int? firstOf(Iterable<String> fields) =>
      fields.isEmpty ? null : fields.map(of).reduce((a, b) => a < b ? a : b);
}

/// Everything the requester has entered, the current step and the submit state. Lives in one Notifier so it
/// survives rebuilds and the time pickers.
class NewRequestState {
  const NewRequestState({
    this.step = RequestSteps.purpose,
    this.purpose = '',
    this.clubId,
    this.date,
    this.start,
    this.end,
    this.attendees = '',
    this.features = const {},
    this.equipment = const {},
    this.budget = '',
    this.notes = '',
    this.attempted = const {},
    this.isSubmitting = false,
    this.serverErrors = const {},
    this.formErrors = const [],
  });

  final int step;
  final String purpose;
  final int? clubId;

  /// A campus date (see core/campus_time.dart).
  final DateTime? date;
  final TimeOfDay? start;
  final TimeOfDay? end;

  /// Text as typed; checked by validators.dart.
  final String attendees;
  final Set<String> features;

  /// Type id → quantity (0 means not requested).
  final Map<int, int> equipment;
  final String budget;
  final String notes;

  /// Steps whose client-side errors are shown (after Continue or Submit).
  final Set<int> attempted;
  final bool isSubmitting;

  /// The server's 400 messages by field. Editing a field clears its message.
  final Map<String, String> serverErrors;

  /// Server messages for no known field, shown on the Review step.
  final List<String> formErrors;

  /// Nullable fields take a function so they can be reset: `copyWith(clubId: () => null)`.
  NewRequestState copyWith({
    int? step,
    String? purpose,
    int? Function()? clubId,
    DateTime? Function()? date,
    TimeOfDay? Function()? start,
    TimeOfDay? Function()? end,
    String? attendees,
    Set<String>? features,
    Map<int, int>? equipment,
    String? budget,
    String? notes,
    Set<int>? attempted,
    bool? isSubmitting,
    Map<String, String>? serverErrors,
    List<String>? formErrors,
  }) =>
      NewRequestState(
        step: step ?? this.step,
        purpose: purpose ?? this.purpose,
        clubId: clubId != null ? clubId() : this.clubId,
        date: date != null ? date() : this.date,
        start: start != null ? start() : this.start,
        end: end != null ? end() : this.end,
        attendees: attendees ?? this.attendees,
        features: features ?? this.features,
        equipment: equipment ?? this.equipment,
        budget: budget ?? this.budget,
        notes: notes ?? this.notes,
        attempted: attempted ?? this.attempted,
        isSubmitting: isSubmitting ?? this.isSubmitting,
        serverErrors: serverErrors ?? this.serverErrors,
        formErrors: formErrors ?? this.formErrors,
      );

  /// The POST body. Only call once [validateDraft] finds no errors.
  NewRequestBody toBody({required bool clubRequired}) => NewRequestBody(
        purpose: purpose,
        attendees: int.parse(attendees.trim()),
        date: date!,
        start: start!,
        end: end!,
        budgetLkr: parseBudget(budget)!,
        requiredFeatures: features.toList()..sort(),
        equipment: {for (final MapEntry(:key, :value) in equipment.entries) if (value > 0) key: value},
        clubId: clubRequired ? clubId : null,
        notes: notes,
      );
}

/// Client-side errors by field (empty when the draft can be sent). Mirrors the DTO annotations and, for the date and
/// times, the live policy (time_rules.dart).
Map<String, String> validateDraft(
  NewRequestState draft, {
  required Eligibility eligibility,
  required PublicPolicy? policy,
  required String? role,
  required DateTime nowUtc,
}) {
  final errors = <String, String>{};
  void check(String field, String? message) {
    if (message != null) errors[field] = message;
  }

  check(RequestFields.purpose, validatePurpose(draft.purpose));
  if (eligibility.clubRequired && !eligibility.clubs.any((c) => c.id == draft.clubId)) {
    check(RequestFields.clubId, 'Choose the club you are booking for');
  }

  final date = draft.date;
  if (policy == null) {
    check(RequestFields.requestedDate, 'The booking rules have not loaded');
  } else if (date == null) {
    check(RequestFields.requestedDate, 'Pick a date');
  } else {
    final first = firstBookableDate(nowUtc, policy);
    final last = lastBookableDate(nowUtc, policy, role);
    if (date.isBefore(first) || date.isAfter(last) || !isOpenDay(date, policy)) {
      check(RequestFields.requestedDate,
          'Pick an open day between ${formatCampusDay(first)} and ${formatCampusDay(last)}');
    }
    final times = validateTimes(date: date, start: draft.start, end: draft.end, policy: policy, nowUtc: nowUtc);
    check(RequestFields.requestedStart, times.start);
    check(RequestFields.requestedEnd, times.end);
  }

  check(RequestFields.attendees, validateAttendees(draft.attendees));
  check(RequestFields.budgetLkr, validateBudget(draft.budget));
  check(RequestFields.notes, validateNotes(draft.notes));
  return errors;
}

/// What happened when the requester pressed Submit.
sealed class SubmitOutcome {
  const SubmitOutcome();
}

/// 201: the saved request.
class SubmitSucceeded extends SubmitOutcome {
  const SubmitSucceeded(this.request);

  final RequestDetail request;
}

/// Client or server field errors; the state now shows them on the first step that has one.
class SubmitInvalid extends SubmitOutcome {
  const SubmitInvalid();
}

/// 409: the open-request cap. [message] is the server's.
class SubmitConflict extends SubmitOutcome {
  const SubmitConflict(this.message);

  final String message;
}

class SubmitFailed extends SubmitOutcome {
  const SubmitFailed(this.problem);

  final Problem problem;
}

class NewRequestNotifier extends Notifier<NewRequestState> {
  @override
  NewRequestState build() => const NewRequestState();

  // Editing a field clears the server's message for it.
  Map<String, String> _without(List<String> fields) => {
        for (final MapEntry(:key, :value) in state.serverErrors.entries)
          if (!fields.any(key.startsWith)) key: value,
      };

  void goTo(int step) => state = state.copyWith(step: step);

  /// The current draft's client-side errors, read fresh (never from an earlier build).
  Map<String, String> _clientErrors(Eligibility eligibility, PublicPolicy? policy) => validateDraft(
        state,
        eligibility: eligibility,
        policy: policy,
        role: ref.read(requesterRoleProvider),
        nowUtc: ref.read(clockProvider)(),
      );

  /// Continue: shows the step's errors, and moves on when it has none.
  void next() {
    final eligibility = ref.read(eligibilityProvider).value;
    if (eligibility == null) return;
    final errors = _clientErrors(eligibility, ref.read(policyProvider).value);
    final step = state.step;
    final blocked = errors.keys.any((f) => RequestSteps.of(f) == step) ||
        state.serverErrors.keys.any((f) => RequestSteps.of(f) == step);
    state = state.copyWith(
      attempted: {...state.attempted, step},
      step: blocked || step == RequestSteps.review ? step : step + 1,
    );
  }

  void setPurpose(String value) =>
      state = state.copyWith(purpose: value, serverErrors: _without([RequestFields.purpose]));

  void setClub(int? id) => state = state.copyWith(clubId: () => id, serverErrors: _without([RequestFields.clubId]));

  void setDate(DateTime date) => state = state.copyWith(
        date: () => campusDate(date),
        serverErrors: _without([RequestFields.requestedStart, RequestFields.requestedEnd]),
      );

  void setStart(TimeOfDay time) =>
      state = state.copyWith(start: () => time, serverErrors: _without([RequestFields.requestedStart]));

  void setEnd(TimeOfDay time) =>
      state = state.copyWith(end: () => time, serverErrors: _without([RequestFields.requestedEnd]));

  void setAttendees(String value) =>
      state = state.copyWith(attendees: value, serverErrors: _without([RequestFields.attendees]));

  void toggleFeature(String code) => state = state.copyWith(
        features: state.features.contains(code) ? ({...state.features}..remove(code)) : {...state.features, code},
        serverErrors: _without([RequestFields.requiredFeatures]),
      );

  /// Clamped to 0–50; 0 removes the line.
  void setQuantity(int typeId, int quantity) => state = state.copyWith(
        equipment: {...state.equipment, typeId: quantity.clamp(0, RequestLimits.maxQuantity)},
        serverErrors: _without([RequestFields.equipment]),
      );

  void setBudget(String value) =>
      state = state.copyWith(budget: value, serverErrors: _without([RequestFields.budgetLkr]));

  void setNotes(String value) => state = state.copyWith(notes: value, serverErrors: _without([RequestFields.notes]));

  /// POSTs the request. Returns null without sending while a submit is pending (a double tap sends once), or when
  /// eligibility or the policy has not loaded or the caller can't submit.
  Future<SubmitOutcome?> submit() async {
    if (state.isSubmitting) return null;
    final eligibility = ref.read(eligibilityProvider).value;
    final policy = ref.read(policyProvider).value;
    if (eligibility == null || policy == null || !eligibility.canSubmit) return null;

    final errors = _clientErrors(eligibility, policy);
    if (errors.isNotEmpty) {
      state = state.copyWith(
        step: RequestSteps.firstOf(errors.keys),
        attempted: {for (var s = 0; s < RequestSteps.count; s++) s},
      );
      return const SubmitInvalid();
    }

    final body = state.toBody(clubRequired: eligibility.clubRequired);
    state = state.copyWith(isSubmitting: true, serverErrors: const {}, formErrors: const []);
    try {
      final created = await ref.read(requestsRepositoryProvider).create(body);
      if (ref.mounted) state = state.copyWith(isSubmitting: false);
      return SubmitSucceeded(created);
    } catch (e) {
      final problem = Problem.from(e);
      if (!ref.mounted) return SubmitFailed(problem);
      if (problem.status == 409) {
        state = state.copyWith(isSubmitting: false);
        return SubmitConflict(problem.title);
      }
      if (problem.status == 400 && problem.fieldErrors.isNotEmpty) {
        final server = {
          for (final MapEntry(:key, :value) in problem.fieldErrors.entries) key: value.join(' '),
        };
        state = state.copyWith(
          isSubmitting: false,
          serverErrors: server,
          formErrors: [
            for (final MapEntry(:key, :value) in server.entries)
              if (RequestSteps.of(key) == RequestSteps.review) value,
          ],
          step: RequestSteps.firstOf(server.keys),
        );
        return const SubmitInvalid();
      }
      state = state.copyWith(isSubmitting: false);
      return SubmitFailed(problem);
    }
  }
}

final newRequestProvider = NotifierProvider.autoDispose<NewRequestNotifier, NewRequestState>(NewRequestNotifier.new);
