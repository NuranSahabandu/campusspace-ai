import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/requests.dart';
import '../../helpers.dart';

void main() {
  late MockRequestsRepository repository;

  setUp(() => repository = MockRequestsRepository());

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

  testWidgets('shows the request, the proposal placeholder and the timeline oldest first', (tester) async {
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
    expect(find.text(RequestDetailScreen.proposalPlaceholder), findsOneWidget);

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
}
