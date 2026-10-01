import 'dart:convert';

import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/proposals.dart';
import '../../fixtures/requests.dart';
import '../../helpers.dart';

Map<String, dynamic> _json(String text) => jsonDecode(text) as Map<String, dynamic>;

/// The lecturer's real request 2 (Mon 26 Oct 2026, 10:00–12:00 campus) with [status].
RequestDetail _lecturerRequestIn(String status) =>
    RequestDetail.fromJson({..._json(requestDetailJson), 'status': status});

/// The lecturer (id 2, the requester of request 2).
const _lecturerId = 2;

void main() {
  late MockRequestsRepository repository;

  setUp(() {
    repository = MockRequestsRepository();
    // An approved request shows its quotation.
    when(() => repository.getQuotation(any())).thenAnswer((_) async => Quotation.fromJson(_json(issuedQuotationJson)));
  });

  final rejected = RequestDetail(
    id: 7,
    purpose: 'Robotics Club workshop',
    status: 'Rejected',
    attendees: 45,
    requestedStart: DateTime.utc(2026, 10, 20, 8, 30),
    requestedEnd: DateTime.utc(2026, 10, 20, 11, 30),
    budgetLkr: 8000,
    notes: 'prefer near the main building',
    requester: const RequesterRef(id: 5, name: 'Kavindi Perera', email: 'kavindi@campusspace.local'),
    club: const ClubRef(id: 1, name: 'Robotics Club'),
    requiredFeatures: const [RequiredFeature(code: 'computers', name: 'Computers')],
    equipment: const [RequestedEquipment(typeId: 1, typeCode: 'MIC-WIRELESS', typeName: 'Wireless microphone', quantity: 2)],
    history: [
      StatusChange(toStatus: 'Submitted', changedById: 5, changedByName: 'Kavindi Perera', changedAt: DateTime.utc(2026, 9, 28, 4, 30)),
      StatusChange(fromStatus: 'Submitted', toStatus: 'AgentProcessing', changedAt: DateTime.utc(2026, 9, 28, 4, 31)),
      StatusChange(
        fromStatus: 'AgentProcessing',
        toStatus: 'Rejected',
        changedById: 9,
        changedByName: 'Mr. Perera',
        reason: 'The lab is closed for exams',
        changedAt: DateTime.utc(2026, 9, 29, 6, 0),
      ),
    ],
    createdAt: DateTime.utc(2026, 9, 28, 4, 30),
  );

  testWidgets('shows the request, why it was rejected and the timeline oldest first', (tester) async {
    when(() => repository.getRequest(7)).thenAnswer((_) async => rejected);
    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/7');

    expect(find.text('Robotics Club workshop'), findsOneWidget);
    expect(find.text('Robotics Club'), findsOneWidget);
    expect(find.text('Tue 20 Oct 2026'), findsOneWidget);
    expect(find.text('14:00–17:00'), findsOneWidget);
    expect(find.text('LKR 8,000.00'), findsOneWidget);
    expect(find.text('Computers'), findsOneWidget);
    expect(find.text('2 × Wireless microphone'), findsOneWidget);
    expect(find.text('prefer near the main building'), findsOneWidget);
    expect(find.text(RequestOutcomeCard.rejectedTitle), findsOneWidget);

    Finder row(int i) => find.byKey(Key('timeline.$i'));
    expect(find.descendant(of: row(0), matching: find.text('Submitted')), findsOneWidget);
    expect(find.descendant(of: row(0), matching: find.text('Mon 28 Sep 2026, 10:00 · You')), findsOneWidget);
    expect(find.descendant(of: row(1), matching: find.text('Processing')), findsOneWidget);
    expect(find.descendant(of: row(1), matching: find.text('Mon 28 Sep 2026, 10:01 · System')), findsOneWidget);
    expect(find.descendant(of: row(2), matching: find.text('Tue 29 Sep 2026, 11:30 · Mr. Perera')), findsOneWidget);
    expect(find.descendant(of: row(2), matching: find.text('The lab is closed for exams')), findsOneWidget);
    expect(tester.getTopLeft(row(0)).dy, lessThan(tester.getTopLeft(row(1)).dy));
    expect(tester.getTopLeft(row(1)).dy, lessThan(tester.getTopLeft(row(2)).dy));
  });

  testWidgets('"You" is matched by id, not by name', (tester) async {
    when(() => repository.getRequest(2)).thenAnswer((_) async => lecturerRequest);
    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/2');

    expect(find.textContaining('· You'), findsOneWidget);
    expect(find.text('Academic booking'), findsOneWidget);
  });

  testWidgets('403 shows the friendly screen without Retry', (tester) async {
    when(() => repository.getRequest(2))
        .thenThrow(httpError('/api/booking-requests/2', 403, body: {'title': 'You can only view your own requests', 'status': 403}));
    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/2');

    expect(find.text(RequestDetailScreen.unavailable), findsOneWidget);
    expect(find.text('Retry'), findsNothing);
    expect(forbiddenJson, contains('"status": 403'));
  });

  testWidgets('404 and a non-numeric id show the same screen', (tester) async {
    when(() => repository.getRequest(99)).thenThrow(httpError('/api/booking-requests/99', 404));
    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/99');
    expect(find.text(RequestDetailScreen.unavailable), findsOneWidget);

    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/abc');
    expect(find.text(RequestDetailScreen.unavailable), findsOneWidget);
    verifyNever(() => repository.getRequest(any(that: isNot(99))));
  });

  testWidgets('another error shows its title; Retry refetches', (tester) async {
    var calls = 0;
    when(() => repository.getRequest(7)).thenAnswer((_) async {
      if (calls++ == 0) throw httpError('/api/booking-requests/7', 503, body: {'title': 'Service unavailable'});
      return rejected;
    });
    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/7');

    expect(find.text('Service unavailable'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(find.text('Robotics Club workshop'), findsOneWidget);
  });

  group('cancel', () {
    final cancelButton = find.byKey(const Key('request.cancel'));
    final lateWarning = find.byKey(const Key('cancel.lateWarning'));
    // Request 2 starts Mon 26 Oct 10:00 campus; free_cancellation_hours is 24.
    final lateBoundary = DateTime.utc(2026, 10, 26, 4, 30).subtract(const Duration(hours: 24));

    setUp(() {
      stubRequestsReferenceData(repository, MockFacilitiesRepository());
    });

    // A fresh ProviderScope each time: re-pumping the same one would keep the cached request.
    Future<void> pumpDetail(WidgetTester tester, {int userId = _lecturerId, DateTime? now}) async {
      await tester.pumpWidget(const SizedBox());
      await pumpRequestsScreens(tester, repository,
          initialLocation: '/requests/2', role: 'Lecturer', userId: userId, now: now);
    }

    Future<void> openDialog(WidgetTester tester) async {
      await tester.tap(cancelButton);
      await tester.pumpAndSettle();
    }

    testWidgets('the button shows only on the owner\'s requests in a cancellable status', (tester) async {
      const statuses = [
        'Submitted', 'AgentProcessing', 'PendingApproval', 'Approved', 'Completed', 'AgentFailed',
        'RevisionRequested', 'Rejected', 'Cancelled',
      ];
      for (final status in statuses) {
        when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn(status));
        await pumpDetail(tester);
        expect(cancelButton, RequestStatuses.cancellable.contains(status) ? findsOneWidget : findsNothing,
            reason: status);
      }

      // Not the owner (an officer's own screens are on the web portal).
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Submitted'));
      await pumpDetail(tester, userId: kavindiId);
      expect(cancelButton, findsNothing);
    });

    testWidgets('the dialog sends the typed reason; Keep request sends nothing', (tester) async {
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Submitted'));
      when(() => repository.cancel(2, reason: any(named: 'reason')))
          .thenAnswer((_) async => RequestDetail.fromJson(_json(cancelledRequestJson)));
      await pumpDetail(tester);

      await openDialog(tester);
      expect(find.text(CancelRequestDialog.title), findsOneWidget);
      expect(find.text('0/500'), findsOneWidget);
      await tester.tap(find.text(CancelRequestDialog.keep));
      await tester.pumpAndSettle();
      verifyNever(() => repository.cancel(any(), reason: any(named: 'reason')));

      await openDialog(tester);
      await tester.enterText(find.byKey(const Key('cancel.reason')), 'Speaker unavailable');
      await tester.pump();
      expect(find.text('19/500'), findsOneWidget);
      await tester.tap(find.byKey(const Key('cancel.confirm')));
      await tester.pumpAndSettle();

      verify(() => repository.cancel(2, reason: 'Speaker unavailable')).called(1);
    });

    testWidgets('success refreshes the detail and My requests and says so', (tester) async {
      var calls = 0;
      when(() => repository.getRequest(2)).thenAnswer((_) async =>
          calls++ == 0 ? _lecturerRequestIn('Submitted') : RequestDetail.fromJson(_json(cancelledRequestJson)));
      when(() => repository.cancel(2, reason: any(named: 'reason')))
          .thenAnswer((_) async => RequestDetail.fromJson(_json(cancelledRequestJson)));
      await tester.pumpWidget(const SizedBox());
      final router = await pumpRequestsScreens(tester, repository,
          initialLocation: '/requests/2', role: 'Lecturer', userId: _lecturerId);
      // My requests sits under the detail in the route stack.
      verify(() => repository.getRequests(any(), page: 1, pageSize: any(named: 'pageSize'))).called(1);

      await openDialog(tester);
      await tester.tap(find.byKey(const Key('cancel.confirm')));
      await tester.pumpAndSettle();

      expect(find.text(CancelRequestButton.cancelled), findsOneWidget);
      expect(calls, 2);
      expect(find.byKey(const Key('request.cancellation')), findsOneWidget);
      expect(cancelButton, findsNothing);

      // Riverpod pauses the hidden list; it reloads once it is back on screen.
      router.pop();
      await tester.pumpAndSettle();
      verify(() => repository.getRequests(any(), page: 1, pageSize: any(named: 'pageSize'))).called(1);
    });

    testWidgets('a 409 shows the server message as sent and reloads the request', (tester) async {
      final conflict = _json(cancelConflictJson);
      var calls = 0;
      when(() => repository.getRequest(2)).thenAnswer((_) async {
        calls++;
        return _lecturerRequestIn('Submitted');
      });
      when(() => repository.cancel(2, reason: any(named: 'reason')))
          .thenThrow(httpError('/api/booking-requests/2/cancel', 409, body: conflict));
      await pumpDetail(tester);

      await openDialog(tester);
      await tester.tap(find.byKey(const Key('cancel.confirm')));
      await tester.pumpAndSettle();

      expect(find.text('The request is already cancelled'), findsOneWidget);
      expect(find.text(conflict['title'] as String), findsOneWidget);
      expect(calls, 2);
    });

    testWidgets('another error shows its title', (tester) async {
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Submitted'));
      when(() => repository.cancel(2, reason: any(named: 'reason')))
          .thenThrow(httpError('/api/booking-requests/2/cancel', 503, body: {'title': 'Service unavailable'}));
      await pumpDetail(tester);

      await openDialog(tester);
      await tester.tap(find.byKey(const Key('cancel.confirm')));
      await tester.pumpAndSettle();

      expect(find.text('Service unavailable'), findsOneWidget);
      expect(cancelButton, findsOneWidget);
    });

    testWidgets('the late warning appears for an approved request only after start − free_cancellation_hours',
        (tester) async {
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Approved'));

      await pumpDetail(tester, now: lateBoundary);
      await openDialog(tester);
      expect(lateWarning, findsNothing);

      await pumpDetail(tester, now: lateBoundary.add(const Duration(minutes: 1)));
      await openDialog(tester);
      expect(lateWarning, findsOneWidget);
      expect(find.text(CancelRequestDialog.lateWarning), findsOneWidget);

      // Submitted is never late, even inside the window.
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Submitted'));
      await pumpDetail(tester, now: lateBoundary.add(const Duration(hours: 1)));
      await openDialog(tester);
      expect(lateWarning, findsNothing);
    });

    testWidgets('a policy that fails to load says the deadline is unknown, with Retry', (tester) async {
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Approved'));
      var calls = 0;
      when(() => repository.getPolicy()).thenAnswer((_) async {
        if (calls++ == 0) throw httpError('/api/policy-settings/public', 503, body: {'title': 'Service unavailable'});
        return livePolicy;
      });
      await pumpDetail(tester, now: lateBoundary.add(const Duration(minutes: 1)));
      await openDialog(tester);
      expect(find.text(CancelRequestDialog.policyFailed), findsOneWidget);
      expect(lateWarning, findsNothing);

      await tester.tap(find.descendant(of: find.byKey(const Key('cancel.policyError')), matching: find.text('Retry')));
      await tester.pumpAndSettle();
      expect(find.text(CancelRequestDialog.policyFailed), findsNothing);
      expect(lateWarning, findsOneWidget);
    });

    testWidgets('the snackbar reports the server\'s late flag', (tester) async {
      when(() => repository.getRequest(2)).thenAnswer((_) async => _lecturerRequestIn('Approved'));
      when(() => repository.cancel(2, reason: any(named: 'reason')))
          .thenAnswer((_) async => RequestDetail.fromJson(_json(lateCancelledJson)));
      await pumpDetail(tester, now: lateBoundary.add(const Duration(hours: 1)));

      await openDialog(tester);
      await tester.tap(find.byKey(const Key('cancel.confirm')));
      await tester.pumpAndSettle();

      expect(find.text(CancelRequestButton.cancelledLate), findsOneWidget);
    });
  });

  group('cancelled detail', () {
    testWidgets('an officer cancellation shows when, who, and the reason in the timeline', (tester) async {
      when(() => repository.getRequest(10)).thenAnswer((_) async => RequestDetail.fromJson(_json(officerCancelledJson)));
      await pumpRequestsScreens(tester, repository, initialLocation: '/requests/10', userId: _lecturerId);

      final info = find.byKey(const Key('request.cancellation'));
      // 04:43:52Z is 10:13 on campus.
      expect(find.descendant(of: info, matching: find.text('Cancelled on Mon 28 Sep 2026, 10:13')), findsOneWidget);
      expect(find.descendant(of: info, matching: find.text(CancellationInfo.byOfficer)), findsOneWidget);
      expect(find.text(CancellationInfo.lateChip), findsNothing);
      expect(find.byKey(const Key('request.cancel')), findsNothing);
      final last = find.byKey(const Key('timeline.1'));
      expect(find.descendant(of: last, matching: find.text('Cancelled')), findsOneWidget);
      expect(find.descendant(of: last, matching: find.text('Mon 28 Sep 2026, 10:13 · Mr. Perera')), findsOneWidget);
      expect(find.descendant(of: last, matching: find.text('Hall reserved for the convocation')), findsOneWidget);
    });

    testWidgets("an owner's late cancellation shows the Late cancellation chip", (tester) async {
      when(() => repository.getRequest(12)).thenAnswer((_) async => RequestDetail.fromJson(_json(lateCancelledJson)));
      await pumpRequestsScreens(tester, repository, initialLocation: '/requests/12', userId: _lecturerId);

      final info = find.byKey(const Key('request.cancellation'));
      expect(find.descendant(of: info, matching: find.widgetWithText(Chip, CancellationInfo.lateChip)), findsOneWidget);
      expect(find.text(CancellationInfo.byOfficer), findsNothing);
      final last = find.byKey(const Key('timeline.2'));
      expect(find.descendant(of: last, matching: find.textContaining('· You')), findsOneWidget);
      expect(find.descendant(of: last, matching: find.text('External examiner unavailable')), findsOneWidget);
    });

    testWidgets('a request that was never cancelled has no cancellation card', (tester) async {
      when(() => repository.getRequest(2)).thenAnswer((_) async => lecturerRequest);
      await pumpRequestsScreens(tester, repository, initialLocation: '/requests/2');

      expect(find.byKey(const Key('request.cancellation')), findsNothing);
    });
  });

  group('while the agent is planning', () {
    final processing = RequestDetail.fromJson(_json(createdRequestJson));
    final pending = RequestDetail.fromJson({..._json(createdRequestJson), 'status': RequestStatuses.pendingApproval});

    testWidgets('an AgentProcessing request re-fetches until the agent moves it, then stops', (tester) async {
      final answers = [processing, pending];
      when(() => repository.getRequest(31)).thenAnswer((_) async => answers.length > 1 ? answers.removeAt(0) : answers.first);
      await pumpRequestsScreens(tester, repository, initialLocation: '/requests/31', userId: _lecturerId);
      expect(find.text('Processing'), findsWidgets);

      await tester.pump(const Duration(seconds: 3));
      await tester.pumpAndSettle();

      expect(find.text('Waiting for approval'), findsWidgets);
      verify(() => repository.getRequest(31)).called(2);
      // Waiting for the officer: every 15 s, not every 3 s.
      await tester.pump(const Duration(seconds: 9));
      verifyNever(() => repository.getRequest(31));
    });

    testWidgets('a request in a settled status is loaded once', (tester) async {
      for (final status in [RequestStatuses.submitted, RequestStatuses.approved, RequestStatuses.rejected]) {
        when(() => repository.getRequest(31))
            .thenAnswer((_) async => RequestDetail.fromJson({..._json(createdRequestJson), 'status': status}));
        await tester.pumpWidget(const SizedBox());
        await pumpRequestsScreens(tester, repository, initialLocation: '/requests/31', userId: _lecturerId);

        await tester.pump(const Duration(minutes: 1));

        verify(() => repository.getRequest(31)).called(1);
      }
    });
  });
}
