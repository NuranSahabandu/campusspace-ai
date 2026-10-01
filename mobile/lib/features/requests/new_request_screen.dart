import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/format.dart';
import '../../core/router.dart';
import '../../core/validators.dart';
import '../notifications/notification_permission.dart';
import '../rooms/room_widgets.dart';
import '../rooms/rooms_providers.dart';
import 'models.dart';
import 'new_request_controller.dart';
import 'requests_providers.dart';
import 'time_rules.dart';

/// New request (plan §13, UC05): a five-step form that saves the request as Submitted.
class NewRequestScreen extends ConsumerStatefulWidget {
  const NewRequestScreen({super.key});

  static const submitted = 'Request submitted';
  static const academic = 'Academic booking (no club)';
  static const notesHelper = 'Anything the reviewer should know, e.g. location preferences';
  static const capTitle = 'Request limit reached';

  @override
  ConsumerState<NewRequestScreen> createState() => _NewRequestScreenState();
}

class _NewRequestScreenState extends ConsumerState<NewRequestScreen> {
  // Initialised from the notifier, which holds the real values.
  late final _purpose = TextEditingController(text: ref.read(newRequestProvider).purpose);
  late final _attendees = TextEditingController(text: ref.read(newRequestProvider).attendees);
  late final _budget = TextEditingController(text: ref.read(newRequestProvider).budget);
  late final _notes = TextEditingController(text: ref.read(newRequestProvider).notes);

  @override
  void dispose() {
    _purpose.dispose();
    _attendees.dispose();
    _budget.dispose();
    _notes.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final outcome = await ref.read(newRequestProvider.notifier).submit();
    if (!mounted) return;
    final messenger = ScaffoldMessenger.of(context);
    switch (outcome) {
      case SubmitSucceeded(:final request):
        ref.invalidate(myRequestsProvider);
        ref.invalidate(eligibilityProvider);
        // The moment a status notice becomes useful: ask once per install (never at launch).
        unawaited(ref.read(notificationPermissionProvider).askOnce());
        messenger.showSnackBar(const SnackBar(content: Text(NewRequestScreen.submitted)));
        // Replace the stepper, so Back from the detail does not return to a sent form.
        context.pushReplacement(AppRoutes.request(request.id));
      case SubmitConflict(:final message):
        ref.invalidate(eligibilityProvider);
        await showDialog<void>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text(NewRequestScreen.capTitle),
            content: Text(message),
            actions: [TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('OK'))],
          ),
        );
      case SubmitFailed(:final problem):
        messenger
          ..hideCurrentSnackBar()
          ..showSnackBar(SnackBar(content: Text(problem.title)));
      case SubmitInvalid() || null:
        break;
    }
  }

  @override
  Widget build(BuildContext context) {
    // Watched here so the draft lives as long as the screen, even while eligibility reloads.
    final state = ref.watch(newRequestProvider);
    final eligibility = ref.watch(eligibilityProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('New request')),
      body: SafeArea(
        child: switch (eligibility) {
          AsyncValue(:final value?) => _form(state, value),
          AsyncValue(:final error?) => MessageView(
            icon: Icons.error_outline,
            message: Problem.from(error).title,
            action: FilledButton.tonal(
              onPressed: () => ref.invalidate(eligibilityProvider),
              child: const Text('Retry'),
            ),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }

  Widget _form(NewRequestState state, Eligibility eligibility) {
    final notifier = ref.read(newRequestProvider.notifier);
    final policy = ref.watch(policyProvider);
    final clientErrors = validateDraft(
      state,
      eligibility: eligibility,
      policy: policy.value,
      role: ref.watch(requesterRoleProvider),
      nowUtc: ref.watch(clockProvider)(),
    );

    // The server's message wins; a client message shows once its step was continued or submitted.
    String? errorFor(String field) =>
        _serverError(state, field) ?? (state.attempted.contains(RequestSteps.of(field)) ? clientErrors[field] : null);

    bool stepHasError(int step) =>
        state.serverErrors.keys.any((f) => RequestSteps.of(f) == step) ||
        (state.attempted.contains(step) && clientErrors.keys.any((f) => RequestSteps.of(f) == step));

    // A vertical Stepper builds every step inside a cross-fade; only the open step needs its fields.
    Step step(int index, String title, Widget content) => Step(
      title: Text(title),
      content: index == state.step ? Align(alignment: Alignment.centerLeft, child: content) : const SizedBox.shrink(),
      isActive: state.step >= index,
      state: stepHasError(index)
          ? StepState.error
          : state.step > index
          ? StepState.complete
          : StepState.indexed,
    );

    final canSubmit = eligibility.canSubmit && policy.hasValue && !state.isSubmitting;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _EligibilityBanner(eligibility),
        Expanded(
          child: Stepper(
            key: const Key('request.stepper'),
            currentStep: state.step,
            onStepTapped: notifier.goTo,
            onStepContinue: notifier.next,
            onStepCancel: () => notifier.goTo(state.step - 1),
            controlsBuilder: (context, details) => details.stepIndex != details.currentStep
                ? const SizedBox.shrink()
                : Padding(
                    padding: const EdgeInsets.only(top: 16),
                    child: Row(
                      children: [
                        if (details.stepIndex == RequestSteps.review)
                          FilledButton(
                            key: const Key('request.submit'),
                            onPressed: canSubmit ? _submit : null,
                            child: state.isSubmitting
                                ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                                : const Text('Submit'),
                          )
                        else
                          FilledButton(onPressed: details.onStepContinue, child: const Text('Continue')),
                        const SizedBox(width: 8),
                        if (details.stepIndex > 0)
                          TextButton(onPressed: details.onStepCancel, child: const Text('Back')),
                      ],
                    ),
                  ),
            steps: [
              step(
                RequestSteps.purpose,
                'Purpose and club',
                _PurposeStep(purpose: _purpose, eligibility: eligibility, clubId: state.clubId, errorFor: errorFor),
              ),
              step(
                RequestSteps.when,
                'Date, time and attendees',
                _WhenStep(state: state, policy: policy, attendees: _attendees, errorFor: errorFor),
              ),
              step(
                RequestSteps.needs,
                'Features and equipment',
                _NeedsStep(
                  state: state,
                  featuresError: _serverError(state, RequestFields.requiredFeatures),
                  equipmentError: _serverError(state, RequestFields.equipment),
                ),
              ),
              step(
                RequestSteps.budget,
                'Budget and notes',
                _BudgetStep(budget: _budget, notes: _notes, errorFor: errorFor),
              ),
              step(RequestSteps.review, 'Review and submit', _ReviewStep(state: state, eligibility: eligibility)),
            ],
          ),
        ),
      ],
    );
  }
}

/// The server's messages for [field], including model-binding keys under it (`equipment[0].Quantity`).
String? _serverError(NewRequestState state, String field) {
  final messages = [
    for (final MapEntry(:key, :value) in state.serverErrors.entries)
      if (key == field || key.startsWith('$field[') || key.startsWith('$field.')) value,
  ];
  return messages.isEmpty ? null : messages.join(' ');
}

typedef _ErrorFor = String? Function(String field);

class _EligibilityBanner extends StatelessWidget {
  const _EligibilityBanner(this.eligibility);

  final Eligibility eligibility;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final count = Text(
      'Open requests: ${eligibility.openRequests} of ${eligibility.maxOpenRequests}',
      key: const Key('request.openCount'),
      style: Theme.of(context).textTheme.bodySmall,
    );
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (!eligibility.canSubmit) ...[
            Container(
              key: const Key('request.banner'),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(color: scheme.errorContainer, borderRadius: BorderRadius.circular(8)),
              child: Row(
                children: [
                  Icon(Icons.block, color: scheme.onErrorContainer),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(
                      eligibility.reason ?? "You can't submit a request right now",
                      style: TextStyle(color: scheme.onErrorContainer),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 8),
          ],
          count,
        ],
      ),
    );
  }
}

class _PurposeStep extends ConsumerWidget {
  const _PurposeStep({required this.purpose, required this.eligibility, required this.clubId, required this.errorFor});

  final TextEditingController purpose;
  final Eligibility eligibility;
  final int? clubId;
  final _ErrorFor errorFor;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(newRequestProvider.notifier);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          key: const Key('request.purpose'),
          controller: purpose,
          onChanged: notifier.setPurpose,
          maxLength: RequestLimits.purposeMax,
          textCapitalization: TextCapitalization.sentences,
          decoration: InputDecoration(
            labelText: 'Purpose',
            hintText: 'e.g. Robotics Club workshop',
            border: const OutlineInputBorder(),
            errorText: errorFor(RequestFields.purpose),
          ),
        ),
        const SizedBox(height: 8),
        if (eligibility.clubRequired)
          DropdownButtonFormField<int>(
            key: const Key('request.club'),
            initialValue: eligibility.clubs.any((c) => c.id == clubId) ? clubId : null,
            onChanged: eligibility.clubs.isEmpty ? null : notifier.setClub,
            items: [for (final c in eligibility.clubs) DropdownMenuItem(value: c.id, child: Text(c.name))],
            decoration: InputDecoration(
              labelText: 'Club',
              hintText: eligibility.clubs.isEmpty ? "You don't represent an active club" : 'Choose a club',
              border: const OutlineInputBorder(),
              errorText: errorFor(RequestFields.clubId),
            ),
          )
        else
          ListTile(
            key: const Key('request.academic'),
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.school_outlined),
            title: const Text(NewRequestScreen.academic),
            subtitle: switch (errorFor(RequestFields.clubId)) {
              final error? => Text(error, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              null => null,
            },
          ),
      ],
    );
  }
}

/// A tappable read-only field for a value chosen in a dialog (date or time).
class _PickerField extends StatelessWidget {
  const _PickerField({
    super.key,
    required this.label,
    required this.value,
    required this.icon,
    required this.onTap,
    this.error,
  });

  final String label;
  final String? value;
  final IconData icon;
  final VoidCallback? onTap;
  final String? error;

  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    child: InputDecorator(
      isEmpty: value == null,
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
        suffixIcon: Icon(icon),
        errorText: error,
        errorMaxLines: 2,
        enabled: onTap != null,
      ),
      child: Text(value ?? ''),
    ),
  );
}

class _WhenStep extends ConsumerWidget {
  const _WhenStep({required this.state, required this.policy, required this.attendees, required this.errorFor});

  final NewRequestState state;
  final AsyncValue<PublicPolicy> policy;
  final TextEditingController attendees;
  final _ErrorFor errorFor;

  // Local midnight for the Material date picker, which works in the phone's calendar.
  static DateTime _pickerDate(DateTime campusDate) => DateTime(campusDate.year, campusDate.month, campusDate.day);

  Future<void> _pickDate(BuildContext context, WidgetRef ref, PublicPolicy policy) async {
    final now = ref.read(clockProvider)();
    final first = firstBookableDate(now, policy);
    final last = lastBookableDate(now, policy, ref.read(requesterRoleProvider));
    final current = state.date;
    final initial = current != null && !current.isBefore(first) && !current.isAfter(last) && isOpenDay(current, policy)
        ? current
        : firstOpenDay(first, last, policy);
    if (initial == null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No open days in the booking window')));
      return;
    }
    final picked = await showDatePicker(
      context: context,
      helpText: 'Booking date (campus time)',
      firstDate: _pickerDate(first),
      lastDate: _pickerDate(last),
      initialDate: _pickerDate(initial),
      currentDate: _pickerDate(campusToday(now)),
      selectableDayPredicate: (day) => isOpenDay(campusDate(day), policy),
    );
    if (picked != null) ref.read(newRequestProvider.notifier).setDate(picked);
  }

  Future<void> _pickTime(BuildContext context, WidgetRef ref, PublicPolicy policy, {required bool isStart}) async {
    final hours = state.date == null ? null : policy.hoursOn(state.date!);
    final initial = (isStart ? state.start : state.end) ??
        state.start ??
        hours?.open ??
        TimeOfDay.fromDateTime(toCampus(ref.read(clockProvider)()));
    final picked = await showTimePicker(
      context: context,
      initialTime: initial,
      helpText: isStart ? 'Start time (campus)' : 'End time (campus)',
      // Campus times are shown as 24-hour times everywhere in the app.
      builder: (context, child) =>
          MediaQuery(data: MediaQuery.of(context).copyWith(alwaysUse24HourFormat: true), child: child!),
    );
    if (picked == null) return;
    final notifier = ref.read(newRequestProvider.notifier);
    isStart ? notifier.setStart(picked) : notifier.setEnd(picked);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = policy.value;
    if (value == null) {
      return policy.hasError
          ? Row(
              children: [
                Expanded(child: Text(Problem.from(policy.error!).title)),
                TextButton(onPressed: () => ref.invalidate(policyProvider), child: const Text('Retry')),
              ],
            )
          : const LinearProgressIndicator();
    }

    final date = state.date;
    final hours = date == null ? null : value.hoursOn(date);
    final textTheme = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _PickerField(
          key: const Key('request.date'),
          label: 'Date',
          value: date == null ? null : formatCampusDay(date),
          icon: Icons.calendar_today_outlined,
          error: errorFor(RequestFields.requestedDate),
          onTap: () => _pickDate(context, ref, value),
        ),
        const SizedBox(height: 12),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: _PickerField(
                key: const Key('request.start'),
                label: 'Start',
                value: state.start == null ? null : formatTimeOfDay(state.start!),
                icon: Icons.schedule,
                error: errorFor(RequestFields.requestedStart),
                onTap: () => _pickTime(context, ref, value, isStart: true),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: _PickerField(
                key: const Key('request.end'),
                label: 'End',
                value: state.end == null ? null : formatTimeOfDay(state.end!),
                icon: Icons.schedule,
                error: errorFor(RequestFields.requestedEnd),
                onTap: () => _pickTime(context, ref, value, isStart: false),
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          [
            if (date != null && hours != null)
              'Open ${formatTimeOfDay(hours.open)}–${formatTimeOfDay(hours.close)} on ${formatWeekday(date)}s.',
            'Times are campus times on a ${value.slotGranularityMinutes}-minute boundary, '
                'at most ${value.maxDurationHours} hours.',
          ].join(' '),
          style: textTheme.bodySmall,
        ),
        const SizedBox(height: 16),
        TextField(
          key: const Key('request.attendees'),
          controller: attendees,
          onChanged: ref.read(newRequestProvider.notifier).setAttendees,
          keyboardType: TextInputType.number,
          inputFormatters: [FilteringTextInputFormatter.digitsOnly],
          decoration: InputDecoration(
            labelText: 'Attendees',
            helperText: '${RequestLimits.minAttendees}–${RequestLimits.maxAttendees}',
            border: const OutlineInputBorder(),
            errorText: errorFor(RequestFields.attendees),
          ),
        ),
      ],
    );
  }
}

class _NeedsStep extends ConsumerWidget {
  const _NeedsStep({required this.state, this.featuresError, this.equipmentError});

  final NewRequestState state;
  final String? featuresError;
  final String? equipmentError;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(newRequestProvider.notifier);
    final features = ref.watch(featuresProvider);
    final types = ref.watch(equipmentTypesProvider);
    final textTheme = Theme.of(context).textTheme;
    final errorStyle = TextStyle(color: Theme.of(context).colorScheme.error);

    Widget loading<T>(AsyncValue<T> value, VoidCallback retry, Widget Function(T data) builder) => switch (value) {
      AsyncValue(:final value?) => builder(value),
      AsyncValue(:final error?) => Row(
        children: [
          Expanded(child: Text(Problem.from(error).title)),
          TextButton(onPressed: retry, child: const Text('Retry')),
        ],
      ),
      _ => const LinearProgressIndicator(),
    };

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Room features', style: textTheme.titleSmall),
        const SizedBox(height: 8),
        loading(
          features,
          () => ref.invalidate(featuresProvider),
          (list) => Wrap(
            spacing: 8,
            runSpacing: 4,
            children: [
              for (final f in list)
                FilterChip(
                  key: Key('feature.${f.code}'),
                  label: Text(f.name),
                  selected: state.features.contains(f.code),
                  onSelected: (_) => notifier.toggleFeature(f.code),
                ),
            ],
          ),
        ),
        if (featuresError != null) Text(featuresError!, style: errorStyle),
        const SizedBox(height: 16),
        Text('Equipment', style: textTheme.titleSmall),
        loading(
          types,
          () => ref.invalidate(equipmentTypesProvider),
          (list) => _EquipmentList(types: list, quantities: state.equipment),
        ),
        if (equipmentError != null) Text(equipmentError!, style: errorStyle),
      ],
    );
  }
}

/// Equipment types grouped by category, each with a 0–50 quantity stepper (0 = not requested).
class _EquipmentList extends ConsumerWidget {
  const _EquipmentList({required this.types, required this.quantities});

  final List<EquipmentType> types;
  final Map<int, int> quantities;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(newRequestProvider.notifier);
    final textTheme = Theme.of(context).textTheme;
    final byCategory = <String, List<EquipmentType>>{};
    for (final t in [...types]..sort((a, b) => a.name.compareTo(b.name))) {
      byCategory.putIfAbsent(t.category, () => []).add(t);
    }
    final categories = byCategory.keys.toList()..sort();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final category in categories) ...[
          Padding(
            padding: const EdgeInsets.only(top: 12, bottom: 4),
            child: Text(category, style: textTheme.labelLarge),
          ),
          for (final t in byCategory[category]!)
            Row(
              key: Key('equipment.${t.id}'),
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(t.name, style: textTheme.bodyLarge),
                      Text('${formatLkr(t.feePerBooking)} per booking', style: textTheme.bodySmall),
                      if (t.coveredByFeatureName != null)
                        Text('Built into rooms with ${t.coveredByFeatureName}', style: textTheme.bodySmall),
                    ],
                  ),
                ),
                IconButton(
                  key: Key('equipment.${t.id}.remove'),
                  tooltip: 'Fewer',
                  icon: const Icon(Icons.remove_circle_outline),
                  onPressed: (quantities[t.id] ?? 0) > 0
                      ? () => notifier.setQuantity(t.id, (quantities[t.id] ?? 0) - 1)
                      : null,
                ),
                SizedBox(
                  width: 28,
                  child: Text(
                    '${quantities[t.id] ?? 0}',
                    key: Key('equipment.${t.id}.quantity'),
                    textAlign: TextAlign.center,
                  ),
                ),
                IconButton(
                  key: Key('equipment.${t.id}.add'),
                  tooltip: 'More',
                  icon: const Icon(Icons.add_circle_outline),
                  onPressed: (quantities[t.id] ?? 0) < RequestLimits.maxQuantity
                      ? () => notifier.setQuantity(t.id, (quantities[t.id] ?? 0) + 1)
                      : null,
                ),
              ],
            ),
        ],
      ],
    );
  }
}

class _BudgetStep extends ConsumerWidget {
  const _BudgetStep({required this.budget, required this.notes, required this.errorFor});

  final TextEditingController budget;
  final TextEditingController notes;
  final _ErrorFor errorFor;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(newRequestProvider.notifier);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          key: const Key('request.budget'),
          controller: budget,
          onChanged: notifier.setBudget,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.]'))],
          decoration: InputDecoration(
            labelText: 'Budget',
            prefixText: 'LKR ',
            border: const OutlineInputBorder(),
            errorText: errorFor(RequestFields.budgetLkr),
          ),
        ),
        const SizedBox(height: 16),
        TextField(
          key: const Key('request.notes'),
          controller: notes,
          onChanged: notifier.setNotes,
          minLines: 3,
          maxLines: 6,
          maxLength: RequestLimits.notesMax,
          textCapitalization: TextCapitalization.sentences,
          decoration: InputDecoration(
            labelText: 'Notes (optional)',
            helperText: NewRequestScreen.notesHelper,
            helperMaxLines: 2,
            border: const OutlineInputBorder(),
            errorText: errorFor(RequestFields.notes),
          ),
        ),
      ],
    );
  }
}

class _ReviewStep extends ConsumerWidget {
  const _ReviewStep({required this.state, required this.eligibility});

  final NewRequestState state;
  final Eligibility eligibility;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final features = ref.watch(featuresProvider).value ?? const [];
    final types = ref.watch(equipmentTypesProvider).value ?? const [];
    final names = {for (final f in features) f.code: f.name};
    final typeNames = {for (final t in types) t.id: t.name};
    final date = state.date;
    final budget = parseBudget(state.budget);
    final club = eligibility.clubRequired
        ? eligibility.clubs.where((c) => c.id == state.clubId).firstOrNull?.name
        : NewRequestScreen.academic;
    final equipment = [
      for (final MapEntry(:key, :value) in state.equipment.entries)
        if (value > 0) '$value × ${typeNames[key] ?? 'Type $key'}',
    ];
    final errorStyle = TextStyle(color: Theme.of(context).colorScheme.error);

    return Column(
      key: const Key('request.review'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SummaryRow('Purpose', state.purpose.trim().isEmpty ? '—' : state.purpose.trim()),
        _SummaryRow('Club', club ?? '—'),
        _SummaryRow('Date', date == null ? '—' : formatCampusDay(date)),
        _SummaryRow(
          'Time',
          state.start == null || state.end == null
              ? '—'
              : '${formatTimeOfDay(state.start!)}–${formatTimeOfDay(state.end!)}',
        ),
        _SummaryRow('Attendees', state.attendees.trim().isEmpty ? '—' : state.attendees.trim()),
        _SummaryRow(
          'Features',
          state.features.isEmpty ? 'None' : [for (final code in state.features) names[code] ?? code].join(', '),
        ),
        _SummaryRow('Equipment', equipment.isEmpty ? 'None' : equipment.join('\n')),
        _SummaryRow('Budget', budget == null ? '—' : formatLkr(budget)),
        _SummaryRow('Notes', state.notes.trim().isEmpty ? '—' : state.notes.trim()),
        for (final message in state.formErrors) Text(message, style: errorStyle),
      ],
    );
  }
}

class _SummaryRow extends StatelessWidget {
  const _SummaryRow(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(width: 96, child: Text(label, style: Theme.of(context).textTheme.labelLarge)),
        Expanded(child: Text(value)),
      ],
    ),
  );
}
