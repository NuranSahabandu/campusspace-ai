import 'dart:async';

import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/new_request_controller.dart';
import 'package:campusspace_mobile/features/requests/new_request_screen.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockRequestsRepository requests;
  late MockFacilitiesRepository facilities;

  setUpAll(() => registerFallbackValue(NewRequestBody(
        purpose: '',
        attendees: 1,
        date: DateTime.utc(2026),
        start: const TimeOfDay(hour: 0, minute: 0),
        end: const TimeOfDay(hour: 0, minute: 0),
        budgetLkr: 0,
      )));

  setUp(() {
    requests = MockRequestsRepository();
    facilities = MockFacilitiesRepository();
  });

  Future<GoRouter> pump(WidgetTester tester, {Eligibility? eligibility, String role = Roles.student}) {
    stubRequestsReferenceData(requests, facilities, eligibility: eligibility);
    return pumpRequestsScreens(tester, requests,
        facilities: facilities, initialLocation: '/requests/new', role: role);
  }

  NewRequestNotifier notifier(WidgetTester tester) => ProviderScope.containerOf(tester.element(find.byType(NewRequestScreen)))
      .read(newRequestProvider.notifier);

  /// The demo request, entered through the notifier (the time pickers are platform dialogs), then the Review step.
  Future<void> fillDemoRequest(WidgetTester tester, {int? clubId = 1}) async {
    notifier(tester)
      ..setPurpose('Robotics Club workshop')
      ..setClub(clubId)
      ..setDate(DateTime.utc(2026, 10, 20))
      ..setStart(const TimeOfDay(hour: 14, minute: 0))
      ..setEnd(const TimeOfDay(hour: 17, minute: 0))
      ..setAttendees('45')
      ..toggleFeature('computers')
      ..toggleFeature('projector')
      ..setQuantity(1, 2)
      ..setQuantity(5, 1)
      ..setQuantity(5, 0)
      ..setBudget('8000')
      ..setNotes('prefer near the main building')
      ..goTo(RequestSteps.review);
    await tester.pumpAndSettle();
  }

  final submit = find.byKey(const Key('request.submit'));
  bool submitEnabled(WidgetTester tester) => tester.widget<FilledButton>(submit).onPressed != null;

  testWidgets('a student picks a club; a lecturer sees an academic booking and no dropdown', (tester) async {
    await pump(tester);
    expect(find.byKey(const Key('request.club')), findsOneWidget);
    await tester.tap(find.byKey(const Key('request.club')));
    await tester.pumpAndSettle();
    expect(find.text('Robotics Club').last, findsOneWidget);
  });

  testWidgets('a lecturer sees "Academic booking (no club)" and no club dropdown', (tester) async {
    await pump(tester, eligibility: lecturerEligibility, role: Roles.lecturer);

    expect(find.byKey(const Key('request.club')), findsNothing);
    expect(find.text(NewRequestScreen.academic), findsOneWidget);
    expect(find.text('Open requests: 1 of 3'), findsOneWidget);
  });

  testWidgets('canSubmit false shows the server reason and disables Submit', (tester) async {
    await pump(tester, eligibility: notRepEligibility);

    expect(find.byKey(const Key('request.banner')), findsOneWidget);
    expect(find.text('You must be the registered representative of an active club'), findsOneWidget);
    expect(find.text('Open requests: 0 of 3'), findsOneWidget);

    await fillDemoRequest(tester);
    expect(find.byKey(const Key('request.review')), findsOneWidget);
    expect(submitEnabled(tester), isFalse);
  });

  testWidgets('Continue on an empty first step shows the errors and stays', (tester) async {
    await pump(tester);

    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();

    expect(find.text('Purpose is required'), findsOneWidget);
    expect(find.text('Choose the club you are booking for'), findsOneWidget);
    expect(find.byKey(const Key('request.purpose')), findsOneWidget);

    await tester.enterText(find.byKey(const Key('request.purpose')), 'Robotics Club workshop');
    notifier(tester).setClub(1);
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('request.date')), findsOneWidget);
  });

  testWidgets('the date picker starts after the lead time and Sunday is not selectable', (tester) async {
    await pump(tester);
    notifier(tester).goTo(RequestSteps.when);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('request.date')));
    await tester.pumpAndSettle();

    final dialog = tester.widget<DatePickerDialog>(find.byType(DatePickerDialog));
    // Now is Monday 28 Sep 10:00 campus; + 48 h is Wednesday 30 Sep. Students book up to 60 days ahead.
    expect(dialog.firstDate, DateTime(2026, 9, 30));
    expect(dialog.lastDate, DateTime(2026, 11, 27));
    expect(dialog.selectableDayPredicate!(DateTime(2026, 10, 25)), isFalse, reason: 'Sunday');
    expect(dialog.selectableDayPredicate!(DateTime(2026, 10, 24)), isTrue, reason: 'Saturday');
  });

  testWidgets('14:15 is rejected inline with the granularity message', (tester) async {
    await pump(tester);
    notifier(tester)
      ..setDate(DateTime.utc(2026, 10, 20))
      ..setStart(const TimeOfDay(hour: 14, minute: 15))
      ..setEnd(const TimeOfDay(hour: 17, minute: 0))
      ..setAttendees('45')
      ..goTo(RequestSteps.when);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();

    expect(find.text('Must be on a 30-minute boundary'), findsOneWidget);
    expect(find.text('Tue 20 Oct 2026'), findsOneWidget);
  });

  testWidgets('the review step summarises everything', (tester) async {
    await pump(tester);
    await fillDemoRequest(tester);

    final review = find.byKey(const Key('request.review'));
    Finder inReview(String text) => find.descendant(of: review, matching: find.text(text));
    expect(inReview('Robotics Club workshop'), findsOneWidget);
    expect(inReview('Robotics Club'), findsOneWidget);
    expect(inReview('Tue 20 Oct 2026'), findsOneWidget);
    expect(inReview('14:00–17:00'), findsOneWidget);
    expect(inReview('45'), findsOneWidget);
    expect(inReview('Computers, Projector'), findsOneWidget);
    expect(inReview('2 × Wireless microphone'), findsOneWidget);
    expect(inReview('LKR 8,000.00'), findsOneWidget);
    expect(inReview('prefer near the main building'), findsOneWidget);
    expect(submitEnabled(tester), isTrue);
  });

  testWidgets('Submit POSTs the body, then shows the new request instead of the form', (tester) async {
    when(() => requests.create(any())).thenAnswer((_) async => lecturerRequest);
    when(() => requests.getRequest(lecturerRequest.id)).thenAnswer((_) async => lecturerRequest);
    final router = await pump(tester);
    await fillDemoRequest(tester);

    await tester.tap(submit);
    await tester.pump();
    expect(find.text(NewRequestScreen.submitted), findsOneWidget);
    await tester.pumpAndSettle();

    final body = verify(() => requests.create(captureAny())).captured.single as NewRequestBody;
    expect(body.toJson(), {
      'purpose': 'Robotics Club workshop',
      'attendees': 45,
      'requestedStart': '2026-10-20T14:00:00+05:30',
      'requestedEnd': '2026-10-20T17:00:00+05:30',
      'budgetLkr': 8000,
      'requiredFeatures': ['computers', 'projector'],
      'equipment': [
        {'typeId': 1, 'quantity': 2},
      ],
      'clubId': 1,
      'notes': 'prefer near the main building',
    });
    expect(find.byType(RequestDetailScreen), findsOneWidget);
    expect(find.byType(NewRequestScreen), findsNothing);
    expect(router.routerDelegate.currentConfiguration.last.matchedLocation, '/requests/${lecturerRequest.id}');
    // Back goes to My requests, not to the sent form.
    router.pop();
    await tester.pumpAndSettle();
    expect(router.routerDelegate.currentConfiguration.last.matchedLocation, '/requests');
  });

  testWidgets('a lecturer sends clubId null', (tester) async {
    when(() => requests.create(any())).thenAnswer((_) async => lecturerRequest);
    when(() => requests.getRequest(any())).thenAnswer((_) async => lecturerRequest);
    await pump(tester, eligibility: lecturerEligibility, role: Roles.lecturer);
    await fillDemoRequest(tester, clubId: 1);

    await tester.tap(submit);
    await tester.pumpAndSettle();

    final body = verify(() => requests.create(captureAny())).captured.single as NewRequestBody;
    final sent = body.toJson();
    expect(sent.containsKey('clubId'), isTrue);
    expect(sent['clubId'], isNull);
  });

  testWidgets('a server 400 on requestedStart jumps to step 2 and shows the message', (tester) async {
    when(() => requests.create(any())).thenThrow(httpError('/api/booking-requests', 400, body: {
      'title': 'Start must be in the future.',
      'status': 400,
      'errors': {
        'RequestedStart': ['Start must be in the future.'],
      },
    }));
    await pump(tester);
    await fillDemoRequest(tester);

    await tester.tap(submit);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('request.start')), findsOneWidget);
    expect(find.text('Start must be in the future.'), findsOneWidget);
    expect(find.byKey(const Key('request.review')), findsNothing);

    // Picking a new start time clears the server's message.
    notifier(tester).setStart(const TimeOfDay(hour: 15, minute: 0));
    await tester.pumpAndSettle();
    expect(find.text('Start must be in the future.'), findsNothing);
  });

  testWidgets('a 409 shows the server message in a dialog', (tester) async {
    const message = 'You already have 3 open requests (the limit is 3)';
    when(() => requests.create(any()))
        .thenThrow(httpError('/api/booking-requests', 409, body: {'title': message, 'status': 409}));
    await pump(tester);
    await fillDemoRequest(tester);

    await tester.tap(submit);
    await tester.pumpAndSettle();

    expect(find.byType(AlertDialog), findsOneWidget);
    expect(find.text(NewRequestScreen.capTitle), findsOneWidget);
    expect(find.text(message), findsOneWidget);
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();
    expect(find.byType(NewRequestScreen), findsOneWidget);
  });

  testWidgets('a double tap on Submit sends one request', (tester) async {
    final response = Completer<RequestDetail>();
    when(() => requests.create(any())).thenAnswer((_) => response.future);
    when(() => requests.getRequest(any())).thenAnswer((_) async => lecturerRequest);
    await pump(tester);
    await fillDemoRequest(tester);

    await tester.tap(submit);
    await tester.tap(submit, warnIfMissed: false);
    await tester.pump();
    expect(submitEnabled(tester), isFalse);
    await tester.tap(submit, warnIfMissed: false);

    response.complete(lecturerRequest);
    await tester.pumpAndSettle();

    verify(() => requests.create(any())).called(1);
  });
}
