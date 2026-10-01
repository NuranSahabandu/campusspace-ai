import 'dart:convert';

import 'package:dio/dio.dart';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/loans/loans_repository.dart';
import 'package:campusspace_mobile/features/notifications/status_notices.dart';
import 'package:campusspace_mobile/features/notifications/status_watcher.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:campusspace_mobile/features/requests/requests_repository.dart';
import 'package:campusspace_mobile/features/rooms/facilities_repository.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/proposals.dart';
import '../../fixtures/requests.dart';
import '../../helpers.dart';

RequestDetail detail(String json) => RequestDetail.fromJson(jsonDecode(json) as Map<String, dynamic>);

void main() {
  late MockRequestsRepository requests;
  late FakeStatusNotifier notifier;

  /// What the list endpoint returns now (the watched statuses only); tests change it between polls.
  late List<RequestSummary> listed;

  PagedResult<RequestSummary> page() =>
      PagedResult(items: listed, page: 1, pageSize: watchedPageSize, total: listed.length);

  void stubList() => when(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
      .thenAnswer((_) async => page());

  setUp(() {
    requests = MockRequestsRepository();
    notifier = FakeStatusNotifier();
    listed = [];
    stubList();
  });

  group('StatusWatcher', () {
    late StatusWatcher watcher;
    late List<int> changed;

    void start() {
      changed = [];
      watcher = StatusWatcher(repository: requests, notifier: notifier, onChanged: changed.add)..start();
    }

    // Stops the watcher's timer before the test ends (the binding checks for pending timers before tearDown runs).
    Future<void> stop(WidgetTester tester) async {
      watcher.stop();
      await tester.pump();
    }

    testWidgets('asks only for the watched statuses, one page of 100', (tester) async {
      start();
      await tester.pump();
      verify(() => requests.getRequests(watchedStatuses, page: 1, pageSize: 100)).called(1);
      await stop(tester);
    });

    testWidgets('the first poll is the baseline; a change on a later poll notifies once', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.pendingApproval)];
      start();
      await tester.pump();
      expect(notifier.shown, isEmpty);

      listed = [testRequest(1, status: RequestStatuses.approved)];
      await tester.pump(const Duration(seconds: 15)); // PendingApproval's interval
      expect(notifier.shown, [const StatusNotice(requestId: 1, kind: NoticeKind.approved, purpose: 'Request 1')]);
      expect(changed, [1]);

      await tester.pump(idlePollInterval); // the same data again
      expect(notifier.shown, hasLength(1));
      await stop(tester);
    });

    testWidgets('the interval follows the statuses: 3 s while the agent works', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.agentProcessing)];
      start();
      await tester.pump();
      listed = [testRequest(1, status: RequestStatuses.agentFailed)];
      await tester.pump(const Duration(seconds: 3));
      expect(notifier.shown.single.kind, NoticeKind.failed);
      await stop(tester);
    });

    testWidgets('nothing in flight polls every 60 s', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.approved)];
      start();
      await tester.pump();
      await tester.pump(const Duration(seconds: 59));
      verify(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize'))).called(1);
      await tester.pump(const Duration(seconds: 1));
      verify(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize'))).called(1);
      await stop(tester);
    });

    testWidgets('a request that leaves the list is looked up once: rejected by an officer', (tester) async {
      listed = [testRequest(37, status: RequestStatuses.pendingApproval)];
      when(() => requests.getRequest(37)).thenAnswer((_) async => detail(rejectedDetailJson));
      start();
      await tester.pump();

      listed = [];
      await tester.pump(const Duration(seconds: 15));
      expect(notifier.shown.single.kind, NoticeKind.rejected);
      expect(watcher.baseline.ids, isEmpty, reason: 'a closed request is no longer tracked');

      await tester.pump(idlePollInterval);
      verify(() => requests.getRequest(37)).called(1);
      await stop(tester);
    });

    testWidgets('closed automatically and cancelled by an officer come from the detail', (tester) async {
      listed = [
        testRequest(41, status: RequestStatuses.pendingApproval),
        testRequest(10, status: RequestStatuses.approved),
      ];
      when(() => requests.getRequest(41)).thenAnswer((_) async => detail(closedAutomaticallyJson));
      when(() => requests.getRequest(10)).thenAnswer((_) async => detail(officerCancelledJson));
      start();
      await tester.pump();

      listed = [];
      await tester.pump(const Duration(seconds: 15));
      expect(notifier.shown.map((n) => (n.requestId, n.kind)),
          [(41, NoticeKind.closed), (10, NoticeKind.cancelledByOfficer)]);
      await stop(tester);
    });

    testWidgets('a request that is gone (404) is dropped without a notice', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.pendingApproval)];
      when(() => requests.getRequest(1)).thenThrow(httpError('/api/booking-requests/1', 404));
      start();
      await tester.pump();
      listed = [];
      await tester.pump(const Duration(seconds: 15));
      expect(notifier.shown, isEmpty);
      expect(watcher.baseline.ids, isEmpty);
      await stop(tester);
    });

    testWidgets('a failed poll keeps the baseline and polls again', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.pendingApproval)];
      start();
      await tester.pump();

      when(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenThrow(DioException.connectionError(
              requestOptions: RequestOptions(path: '/api/booking-requests'), reason: 'refused'));
      await tester.pump(const Duration(seconds: 15));
      expect(notifier.shown, isEmpty);

      listed = [testRequest(1, status: RequestStatuses.approved)];
      stubList();
      await tester.pump(idlePollInterval);
      expect(notifier.shown.single.kind, NoticeKind.approved);
      await stop(tester);
    });

    testWidgets('stop ends polling and clears the baseline', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.agentProcessing)];
      start();
      await tester.pump();
      watcher.stop();
      expect(watcher.baseline.hasBaseline, isFalse);
      await tester.pump(idlePollInterval);
      verify(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize'))).called(1);
    });
  });

  group('statusWatcherProvider in the app', () {
    late MockAuthRepository auth;
    late MockFacilitiesRepository facilities;

    setUp(() {
      auth = MockAuthRepository();
      facilities = MockFacilitiesRepository();
      stubRequestsReferenceData(requests, facilities);
      stubList();
      when(() => requests.getRequest(any())).thenAnswer((_) async => lecturerRequest);
    });

    Future<void> pumpAs(WidgetTester tester, String role) async {
      when(() => auth.me()).thenAnswer((_) async => userWithRole(role));
      await pumpApp(tester, FakeTokenStorage(sessionFor(role)), auth,
          notifier: notifier,
          statusWatcher: true,
          overrides: [
            requestsRepositoryProvider.overrideWithValue(requests),
            facilitiesRepositoryProvider.overrideWithValue(facilities),
          ]);
    }

    Future<void> unmount(WidgetTester tester) => tester.pumpWidget(const SizedBox());

    testWidgets('a signed-in student is watched; logout cancels the shown notices', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.pendingApproval)];
      await pumpAs(tester, Roles.student);
      listed = [testRequest(1, status: RequestStatuses.approved)];
      await tester.pump(const Duration(seconds: 15));
      expect(notifier.shown.single.kind, NoticeKind.approved);

      await tester.tap(find.byTooltip('Log out'));
      await tester.pumpAndSettle();
      expect(notifier.cancelAllCalls, 1);
      expect(notifier.shown, isEmpty);
      await unmount(tester);
    });

    testWidgets('signing in again takes a new baseline (nothing is notified at login)', (tester) async {
      listed = [testRequest(1, status: RequestStatuses.pendingApproval)];
      await pumpAs(tester, Roles.lecturer);
      await tester.tap(find.byTooltip('Log out'));
      await tester.pumpAndSettle();

      listed = [testRequest(1, status: RequestStatuses.approved)];
      when(() => auth.login(any(), any())).thenAnswer((_) async => sessionFor(Roles.lecturer));
      await tester.enterText(find.byKey(const Key('login.email')), 'lecturer@campusspace.local');
      await tester.enterText(find.byKey(const Key('login.password')), 'CampusSpace#2026');
      await tester.tap(find.text('Sign in'));
      await tester.pumpAndSettle();
      await tester.pump(idlePollInterval);
      expect(notifier.shown, isEmpty);
      await unmount(tester);
    });

    testWidgets('a lab technician is not watched', (tester) async {
      final loans = MockLoansRepository();
      when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
      when(() => auth.me()).thenAnswer((_) async => userWithRole(Roles.labTechnician));
      await pumpApp(tester, FakeTokenStorage(sessionFor(Roles.labTechnician)), auth,
          notifier: notifier,
          statusWatcher: true,
          overrides: [
            requestsRepositoryProvider.overrideWithValue(requests),
            loansRepositoryProvider.overrideWithValue(loans),
          ]);
      await tester.pump(idlePollInterval);
      verifyNever(() => requests.getRequests(watchedStatuses, page: 1, pageSize: watchedPageSize));
      await unmount(tester);
    });

    testWidgets('tapping a notice while the app runs opens that request', (tester) async {
      await pumpAs(tester, Roles.lecturer);
      notifier.tap(lecturerRequest.id);
      await tester.pumpAndSettle();
      expect(find.byType(RequestDetailScreen), findsOneWidget);
      verify(() => requests.getRequest(lecturerRequest.id)).called(greaterThanOrEqualTo(1));
      await unmount(tester);
    });
  });
}
