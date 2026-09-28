import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/router.dart';
import '../rooms/room_widgets.dart';
import 'loan_widgets.dart';
import 'loans_providers.dart';
import 'loans_repository.dart';
import 'models.dart';
import 'photo_picker.dart';

/// UC11: take an item back with its condition, a note and a photo. Damaged needs both (checked here as the server
/// does; the server still decides). A loan that is already checked in is shown read-only.
class CheckInScreen extends ConsumerWidget {
  const CheckInScreen({super.key, required this.loanId});

  static const unavailable = "This loan isn't available";

  /// Null when the route's id is not a number.
  final int? loanId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final id = loanId;
    Widget frame(Widget child) => Scaffold(
      appBar: AppBar(title: const Text('Check in')),
      body: SafeArea(child: child),
    );
    const notAvailable = MessageView(icon: Icons.lock_outline, message: unavailable);
    if (id == null) return frame(notAvailable);

    return frame(switch (ref.watch(loanProvider(id))) {
      AsyncValue(:final value?) => value.isOpen ? CheckInForm(loan: value) : ReturnedLoanView(loan: value),
      AsyncValue(:final error?) => switch (Problem.from(error)) {
        Problem(status: 404) => notAvailable,
        _ => LoadErrorView(error: error, onRetry: () => ref.invalidate(loanProvider(id))),
      },
      _ => const Center(child: CircularProgressIndicator()),
    });
  }
}

/// Asset tag, type, room and due time of a loan.
class _LoanHeader extends StatelessWidget {
  const _LoanHeader(this.loan);

  final Loan loan;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(loan.assetTag, style: textTheme.headlineSmall),
        Text('${loan.typeCode} · Room ${loan.roomCode}'),
        Text(
          'Out since ${formatCampusDateTime(loan.checkedOutAt)} · by ${loan.checkedOutByName}',
          style: textTheme.bodySmall,
        ),
        Row(
          children: [
            Flexible(child: Text('Due ${formatCampusDateTime(loan.dueAt)}', style: textTheme.bodySmall)),
            if (loan.isOverdue) ...[const SizedBox(width: 8), const FlagChip('Overdue')],
          ],
        ),
      ],
    );
  }
}

/// A loan that was already checked in (for example opened from a stale list): what came back, and no form.
class ReturnedLoanView extends StatelessWidget {
  const ReturnedLoanView({super.key, required this.loan});

  static const alreadyCheckedIn = 'Already checked in';

  final Loan loan;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _LoanHeader(loan),
        const SizedBox(height: 16),
        Card(
          key: const Key('checkIn.returned'),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const Icon(Icons.task_alt),
                    const SizedBox(width: 8),
                    Text(alreadyCheckedIn, style: textTheme.titleMedium),
                  ],
                ),
                const SizedBox(height: 8),
                Text('${formatCampusDateTime(loan.checkedInAt!)} · by ${loan.checkedInByName ?? '—'}'),
                Text('Condition: ${ItemConditions.label(loan.returnCondition ?? '')}'),
                if (loan.damageNote case final note?) ...[
                  const SizedBox(height: 8),
                  // Untrusted text: shown as plain text only.
                  Text(note, style: const TextStyle(fontStyle: FontStyle.italic)),
                ],
                if (loan.hasPhoto) ...[
                  const SizedBox(height: 8),
                  const Row(
                    children: [Icon(Icons.photo_camera_outlined, size: 18), SizedBox(width: 6), Text('Photo on file')],
                  ),
                ],
                if (loan.isLateReturn) ...[const SizedBox(height: 8), const FlagChip('Late return')],
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// The check-in form for an open loan.
class CheckInForm extends ConsumerStatefulWidget {
  const CheckInForm({super.key, required this.loan});

  static const submit = 'Check in';
  static const takePhoto = 'Take photo';
  static const chooseFromGallery = 'Choose from gallery';

  final Loan loan;

  @override
  ConsumerState<CheckInForm> createState() => _CheckInFormState();
}

class _CheckInFormState extends ConsumerState<CheckInForm> {
  final _formKey = GlobalKey<FormState>();
  final _note = TextEditingController();
  String _condition = ItemConditions.good;
  PickedPhoto? _photo;
  String? _photoError;
  Problem? _serverErrors;
  bool _submitting = false;

  bool get _damaged => _condition == ItemConditions.damaged;

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  Future<void> _pick(PhotoSource source) async {
    final messenger = ScaffoldMessenger.of(context);
    try {
      final photo = await ref.read(photoPickerProvider).pick(source);
      if (photo == null || !mounted) return;
      // The server accepts only JPEG or PNG up to 5 MB (by the bytes, not the name). Say so before uploading.
      final error = CheckInRules.photoError(photo.bytes);
      setState(() {
        _photoError = error;
        if (error == null) _photo = photo;
      });
    } catch (_) {
      showMessage(messenger, source == PhotoSource.camera ? 'Could not open the camera' : 'Could not open the gallery');
    }
  }

  Future<void> _submit() async {
    if (_submitting) return;
    final noteOk = _formKey.currentState!.validate();
    final photo = _photo;
    final photoError = photo != null
        ? CheckInRules.photoError(photo.bytes)
        : _damaged
        ? CheckInRules.damagedPhotoRequired
        : null;
    setState(() {
      _photoError = photoError;
      _serverErrors = null;
    });
    if (!noteOk || photoError != null) return;

    // Set before the first await, so a second tap in the same frame sends nothing.
    setState(() => _submitting = true);
    final loan = widget.loan;
    final container = ProviderScope.containerOf(context, listen: false);
    final messenger = ScaffoldMessenger.of(context);
    final router = GoRouter.of(context);
    try {
      final result = await container
          .read(loansRepositoryProvider)
          .checkIn(loan.id, condition: _condition, note: _note.text, photo: photo);
      invalidateLoanLists(container, bookingId: loan.bookingId, loanId: loan.id);
      showMessage(
        messenger,
        result.returnCondition == ItemConditions.damaged
            ? 'Checked in ${loan.assetTag}. It was sent to repair.'
            : 'Checked in ${loan.assetTag}',
      );
      if (router.canPop()) {
        router.pop();
      } else {
        router.go(AppRoutes.home);
      }
    } catch (e) {
      final problem = Problem.from(e);
      if (problem.status == 400 && problem.fieldErrors.isNotEmpty) {
        if (mounted) setState(() => _serverErrors = problem);
      } else {
        showMessage(messenger, problem.title);
        // Someone else checked it in meanwhile: reload, which shows the read-only view.
        if (problem.status == 409) container.invalidate(loanProvider(loan.id));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final scheme = Theme.of(context).colorScheme;
    final photo = _photo;
    final photoError = _photoError ?? _serverErrors?.fieldError('photo');
    final pickButtonsEnabled = !_submitting;

    return Form(
      key: _formKey,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          _LoanHeader(widget.loan),
          const SizedBox(height: 24),
          DropdownButtonFormField<String>(
            key: const Key('checkIn.condition'),
            initialValue: _condition,
            decoration: InputDecoration(
              labelText: 'Condition',
              border: const OutlineInputBorder(),
              errorText: _serverErrors?.fieldError('condition'),
            ),
            items: [
              for (final c in ItemConditions.all) DropdownMenuItem(value: c, child: Text(ItemConditions.label(c))),
            ],
            onChanged: _submitting ? null : (value) => setState(() => _condition = value ?? _condition),
          ),
          const SizedBox(height: 16),
          TextFormField(
            key: const Key('checkIn.note'),
            controller: _note,
            enabled: !_submitting,
            maxLength: CheckInRules.noteMaxLength,
            maxLines: 4,
            minLines: 2,
            decoration: InputDecoration(
              labelText: _damaged ? 'Note (required for damage)' : 'Note (optional)',
              border: const OutlineInputBorder(),
              errorText: _serverErrors?.fieldError('note'),
            ),
            validator: (value) =>
                _damaged && (value == null || value.trim().isEmpty) ? CheckInRules.damagedNoteRequired : null,
          ),
          const SizedBox(height: 8),
          Text(_damaged ? 'Photo (required for damage)' : 'Photo (optional)', style: textTheme.titleSmall),
          const SizedBox(height: 8),
          if (photo != null) ...[
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.memory(
                photo.bytes,
                key: const Key('checkIn.thumbnail'),
                height: 180,
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => const SizedBox(height: 180, child: Center(child: Icon(Icons.image))),
              ),
            ),
            Row(
              children: [
                TextButton.icon(
                  onPressed: pickButtonsEnabled ? () => _pick(PhotoSource.camera) : null,
                  icon: const Icon(Icons.photo_camera_outlined),
                  label: const Text('Retake'),
                ),
                TextButton.icon(
                  onPressed: pickButtonsEnabled ? () => setState(() => _photo = null) : null,
                  icon: const Icon(Icons.delete_outline),
                  label: const Text('Remove'),
                ),
              ],
            ),
          ] else
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                OutlinedButton.icon(
                  onPressed: pickButtonsEnabled ? () => _pick(PhotoSource.camera) : null,
                  icon: const Icon(Icons.photo_camera_outlined),
                  label: const Text(CheckInForm.takePhoto),
                ),
                OutlinedButton.icon(
                  onPressed: pickButtonsEnabled ? () => _pick(PhotoSource.gallery) : null,
                  icon: const Icon(Icons.photo_library_outlined),
                  label: const Text(CheckInForm.chooseFromGallery),
                ),
              ],
            ),
          if (photoError != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                photoError,
                key: const Key('checkIn.photoError'),
                style: TextStyle(color: scheme.error),
              ),
            ),
          const SizedBox(height: 24),
          FilledButton.icon(
            key: const Key('checkIn.submit'),
            onPressed: _submitting ? null : _submit,
            icon: _submitting
                ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                : const Icon(Icons.assignment_return_outlined),
            label: const Text(CheckInForm.submit),
          ),
        ],
      ),
    );
  }
}
