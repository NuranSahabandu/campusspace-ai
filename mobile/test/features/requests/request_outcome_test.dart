import 'dart:convert';

import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/proposals.dart';
import '../../helpers.dart';

Map<String, dynamic> _json(String text) => jsonDecode(text) as Map<String, dynamic>;

RequestDetail _detail(String json, [Map<String, dynamic> overrides = const {}]) =>
    RequestDetail.fromJson({..._json(json), ...overrides});

/// Where the request stands (UC05/UC06), over real API responses (test/fixtures/proposals.dart). Campus times are
/// asserted as text, so this file also runs under another time zone in CI (TZ=America/New_York).
void main() {
  late MockRequestsRepository repository;
  final title = find.byKey(const Key('outcome.title'));
  final reason = find.byKey(const Key('outcome.reason'));

  setUp(() => repository = MockRequestsRepository());

  Future<int> show(WidgetTester tester, RequestDetail detail) async {
    when(() => repository.getRequest(detail.id)).thenAnswer((_) async => detail);
    await tester.pumpWidget(const SizedBox());
    await pumpRequestsScreens(tester, repository, initialLocation: '/requests/${detail.id}', userId: detail.requester.id);
    return detail.id;
  }

  String textOf(WidgetTester tester, Finder finder) => tester.widget<Text>(finder).data!;

  testWidgets('PendingApproval shows the proposed room and the Draft total, waiting for the officer', (tester) async {
    final detail = _detail(pendingProposalJson);
    await show(tester, detail);

    expect(textOf(tester, title), 'Proposed: A301 · Computer Lab A301, LKR 5,500.00');
    expect(find.text(RequestOutcomeCard.waiting), findsOneWidget);
    verifyNever(() => repository.getQuotation(any()));
  });

  testWidgets('an exempt proposal says Fee-exempt instead of a price', (tester) async {
    final detail = _detail(pendingProposalJson, {
      'latestProposal': {..._json(pendingProposalJson)['latestProposal'] as Map<String, dynamic>, 'total': 0, 'exempt': true},
    });
    await show(tester, detail);

    expect(textOf(tester, title), 'Proposed: A301 · Computer Lab A301, Fee-exempt');
  });

  testWidgets('Approved shows the room, the campus time and the .NET quotation', (tester) async {
    final detail = _detail(approvedDetailJson);
    when(() => repository.getQuotation(detail.id))
        .thenAnswer((_) async => Quotation.fromJson(_json(issuedQuotationJson)));
    await show(tester, detail);

    expect(textOf(tester, title), RequestOutcomeCard.approvedTitle);
    expect(find.byKey(const Key('outcome.room')), findsOneWidget);
    expect(textOf(tester, find.byKey(const Key('outcome.room'))), 'A301 · Computer Lab A301');
    // 08:30Z is 14:00 on campus, whatever the device's time zone.
    expect(find.text('Thu 19 Nov 2026 · 14:00–17:00'), findsOneWidget);
    final quotation = find.byKey(const Key('request.quotation'));
    expect(find.descendant(of: quotation, matching: find.text('Computer lab A301, 3 h @ LKR 1,500')), findsOneWidget);
    expect(find.descendant(of: quotation, matching: find.text('LKR 4,500.00')), findsOneWidget);
    expect(find.descendant(of: quotation, matching: find.text('Wireless microphone x2 @ LKR 500')), findsOneWidget);
    expect(find.descendant(of: find.byKey(const Key('quotation.total')), matching: find.text('LKR 5,500.00')), findsOneWidget);
    expect(find.textContaining('Discount'), findsNothing);
    verify(() => repository.getQuotation(detail.id)).called(1);
  });

  testWidgets('an exempt quotation shows the discount and its reason, total 0', (tester) async {
    final detail = _detail(approvedDetailJson);
    // The real Issued quote, as the lecturer exemption would price it (every line priced, discount = subtotal).
    when(() => repository.getQuotation(detail.id)).thenAnswer((_) async => Quotation.fromJson({
          ..._json(issuedQuotationJson),
          'discount': 5500.00,
          'discountReason': 'Lecturer exemption (academic use)',
          'exempt': true,
          'total': 0.00,
        }));
    await show(tester, detail);

    expect(find.text('Discount (Lecturer exemption (academic use))'), findsOneWidget);
    expect(find.text('−LKR 5,500.00'), findsOneWidget);
    expect(find.text('Total (Fee-exempt)'), findsOneWidget);
    expect(find.descendant(of: find.byKey(const Key('quotation.total')), matching: find.text('LKR 0.00')), findsOneWidget);
  });

  testWidgets('a quotation error offers Retry', (tester) async {
    final detail = _detail(approvedDetailJson);
    var calls = 0;
    when(() => repository.getQuotation(detail.id)).thenAnswer((_) async {
      if (calls++ == 0) throw httpError('/quotation', 503, body: {'title': 'Service unavailable'});
      return Quotation.fromJson(_json(issuedQuotationJson));
    });
    await show(tester, detail);

    expect(find.text('Quotation unavailable: Service unavailable'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('request.quotation')), findsOneWidget);
  });

  testWidgets('Rejected by the officer shows "Rejected by Facilities" and the reason as plain text', (tester) async {
    await show(tester, _detail(rejectedDetailJson));

    expect(textOf(tester, title), RequestOutcomeCard.rejectedTitle);
    expect(textOf(tester, reason), 'The New Building labs are reserved for exams that week.');
  });

  testWidgets('Rejected with no actor is "Closed automatically" with the server reason', (tester) async {
    await show(tester, _detail(closedAutomaticallyJson));

    expect(textOf(tester, title), RequestOutcomeCard.closedTitle);
    expect(textOf(tester, reason),
        'The requested time is no longer valid: Can be booked at most 30 days ahead (as of submission)');
    expect(find.text(RequestOutcomeCard.rejectedTitle), findsNothing);
  });

  testWidgets('AgentFailed explains that Facilities can retry', (tester) async {
    await show(tester, _detail(agentFailedDetailJson));

    expect(textOf(tester, title),
        "We couldn't prepare a proposal: Agent service unreachable (last error: network error). Facilities can retry.");
  });

  testWidgets("after an officer's revise: a new proposal is being prepared, with the officer's notes", (tester) async {
    await show(tester, _detail(revisingDetailJson));

    expect(textOf(tester, title), RequestOutcomeCard.revising);
    expect(textOf(tester, reason), 'Use a lab in the New Building');
  });

  testWidgets('RevisionRequested itself reads the same', (tester) async {
    final json = _json(revisingDetailJson);
    final history = [...json['history'] as List]..removeLast();
    await show(tester, _detail(revisingDetailJson, {'status': 'RevisionRequested', 'history': history}));

    expect(textOf(tester, title), RequestOutcomeCard.revising);
  });

  testWidgets('a first AgentProcessing (no revise) keeps the placeholder', (tester) async {
    final json = _json(revisingDetailJson);
    final history = (json['history'] as List).take(2).toList();
    await show(tester, _detail(revisingDetailJson, {'history': history}));

    expect(textOf(tester, title), RequestDetailScreen.proposalPlaceholder);
  });

  group('refresh while the screen is open', () {
    testWidgets('PendingApproval re-fetches every 15 s, not every 3 s', (tester) async {
      final id = await show(tester, _detail(pendingProposalJson));

      await tester.pump(const Duration(seconds: 3));
      await tester.pump(const Duration(seconds: 11));
      verify(() => repository.getRequest(id)).called(1);
      await tester.pump(const Duration(seconds: 1));
      verify(() => repository.getRequest(id)).called(1);
    });

    testWidgets('AgentProcessing after a revise re-fetches every 3 s', (tester) async {
      final id = await show(tester, _detail(revisingDetailJson));

      await tester.pump(const Duration(seconds: 3));
      await tester.pump(const Duration(seconds: 3));
      verify(() => repository.getRequest(id)).called(3);
    });

    testWidgets('Approved, Rejected and AgentFailed never re-fetch', (tester) async {
      when(() => repository.getQuotation(any())).thenAnswer((_) async => Quotation.fromJson(_json(issuedQuotationJson)));
      for (final json in [approvedDetailJson, rejectedDetailJson, agentFailedDetailJson]) {
        final id = await show(tester, _detail(json));
        await tester.pump(const Duration(minutes: 1));
        verify(() => repository.getRequest(id)).called(1);
      }
    });

    testWidgets('closing the screen stops the refresh', (tester) async {
      final id = await show(tester, _detail(pendingProposalJson));

      await tester.pumpWidget(const SizedBox());
      await tester.pump(const Duration(minutes: 1));
      verify(() => repository.getRequest(id)).called(1);
    });
  });
}
