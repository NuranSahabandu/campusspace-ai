import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/my_requests_screen.dart';
import 'package:campusspace_mobile/features/requests/new_request_screen.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockRequestsRepository repository;

  setUp(() => repository = MockRequestsRepository());

  void stubPages(Future<PagedResult<RequestSummary>> Function(List<String> statuses, int page) answer) =>
      when(() => repository.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((i) => answer(i.positionalArguments[0] as List<String>, i.namedArguments[#page] as int));

  testWidgets('shows each request with campus time, club or Academic, attendees and a status chip', (tester) async {
    stubPages((_, _) async => PagedResult(
          items: [
            testRequest(1),
            testRequest(2, status: RequestStatuses.pendingApproval, clubName: null),
            testRequest(3, status: RequestStatuses.revisionRequested),
          ],
          page: 1,
          pageSize: 20,
          total: 3,
        ));
    await pumpRequestsScreens(tester, repository);

    final first = find.byKey(const Key('request.1'));
    expect(find.descendant(of: first, matching: find.text('Request 1')), findsOneWidget);
    expect(find.descendant(of: first, matching: find.text('Tue 20 Oct 2026 · 14:00–17:00')), findsOneWidget);
    expect(find.descendant(of: first, matching: find.text('Robotics Club · 41 attendees')), findsOneWidget);
    expect(find.descendant(of: first, matching: find.text('Submitted')), findsOneWidget);
    expect(find.text('Academic · 42 attendees'), findsOneWidget);
    expect(find.text('Waiting for approval'), findsOneWidget);
    expect(find.text('Needs revision'), findsOneWidget);
    verify(() => repository.getRequests(const [], page: 1, pageSize: 20)).called(1);
  });

  test('every status has a friendly label and its own colour', () {
    const statuses = [
      RequestStatuses.submitted,
      RequestStatuses.agentProcessing,
      RequestStatuses.pendingApproval,
      RequestStatuses.approved,
      RequestStatuses.completed,
      RequestStatuses.revisionRequested,
      RequestStatuses.rejected,
      RequestStatuses.cancelled,
      RequestStatuses.agentFailed,
    ];
    expect(statuses.map(RequestStatuses.label), [
      'Submitted',
      'Processing',
      'Waiting for approval',
      'Approved',
      'Completed',
      'Needs revision',
      'Rejected',
      'Cancelled',
      'Failed',
    ]);
    expect(statuses.map(RequestStatuses.color).toSet(), hasLength(statuses.length));
  });

  testWidgets('each filter chip sends its statuses', (tester) async {
    stubPages((_, _) async => requestsPage(1, 1));
    await pumpRequestsScreens(tester, repository);

    final expected = {
      RequestStatusFilter.inProgress: ['Submitted', 'AgentProcessing', 'PendingApproval', 'RevisionRequested'],
      RequestStatusFilter.approved: ['Approved', 'Completed'],
      RequestStatusFilter.closed: ['Rejected', 'Cancelled', 'AgentFailed'],
      RequestStatusFilter.all: <String>[],
    };
    for (final MapEntry(key: filter, value: statuses) in expected.entries) {
      await tester.tap(find.byKey(Key('requests.filter.${filter.name}')));
      await tester.pumpAndSettle();
      verify(() => repository.getRequests(statuses, page: 1, pageSize: 20)).called(greaterThanOrEqualTo(1));
    }
  });

  testWidgets('scrolling to the end loads page 2', (tester) async {
    stubPages((_, page) async => requestsPage(page, 25));
    await pumpRequestsScreens(tester, repository);

    await tester.scrollUntilVisible(find.text('Request 25'), 300, scrollable: find.byType(Scrollable).first);
    await tester.pumpAndSettle();

    verify(() => repository.getRequests(const [], page: 2, pageSize: 20)).called(1);
  });

  testWidgets('empty: "No requests yet" with a New request button that opens the form', (tester) async {
    final facilities = MockFacilitiesRepository();
    stubRequestsReferenceData(repository, facilities);
    await pumpRequestsScreens(tester, repository, facilities: facilities);

    expect(find.text(MyRequestsScreen.empty), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'New request'));
    await tester.pumpAndSettle();
    expect(find.byType(NewRequestScreen), findsOneWidget);
  });

  testWidgets('the floating button opens the form', (tester) async {
    final facilities = MockFacilitiesRepository();
    stubRequestsReferenceData(repository, facilities);
    stubPages((_, _) async => requestsPage(1, 2));
    await pumpRequestsScreens(tester, repository, facilities: facilities);

    await tester.tap(find.byKey(const Key('requests.new')));
    await tester.pumpAndSettle();
    expect(find.byType(NewRequestScreen), findsOneWidget);
  });

  testWidgets('error: shows the Problem title; Retry refetches', (tester) async {
    var calls = 0;
    stubPages((_, _) async {
      if (calls++ == 0) throw httpError('/api/booking-requests', 503, body: {'title': 'Service unavailable'});
      return requestsPage(1, 1);
    });
    await pumpRequestsScreens(tester, repository);

    expect(find.text('Service unavailable'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(find.text('Request 1'), findsOneWidget);
  });

  testWidgets('tapping a card opens the request detail', (tester) async {
    stubPages((_, _) async => requestsPage(1, 1));
    when(() => repository.getRequest(1)).thenAnswer((_) async => lecturerRequest);
    await pumpRequestsScreens(tester, repository);

    await tester.tap(find.text('Request 1'));
    await tester.pumpAndSettle();
    expect(find.byType(RequestDetailScreen), findsOneWidget);
  });
}
