import 'package:campusspace_mobile/features/loans/handovers_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockLoansRepository loans;

  setUp(() => loans = MockLoansRepository());

  testWidgets('one card per booking: campus time range, room, requester and per-type counts', (tester) async {
    when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
    await pumpLoansScreens(tester, loans);

    expect(find.text(TodayHandoversView.title), findsOneWidget);
    // 02:05Z–04:05Z and 05:15Z–07:15Z are 07:35–09:35 and 10:45–12:45 on campus, whatever the host's time zone.
    final first = find.byKey(const Key('handover.10'));
    final second = find.byKey(const Key('handover.9'));
    expect(find.descendant(of: first, matching: find.text('07:35–09:35 · A305')), findsOneWidget);
    expect(find.descendant(of: first, matching: find.text('MIC-WIRELESS: 1 / 1 out · 0 returned')), findsOneWidget);
    expect(find.descendant(of: second, matching: find.text('10:45–12:45 · A301')), findsOneWidget);
    expect(find.descendant(of: second, matching: find.text('Dr. Nimal Fernando')), findsOneWidget);
    expect(find.descendant(of: second, matching: find.text('MIC-WIRELESS: 1 / 2 out · 2 returned')), findsOneWidget);
    expect(tester.getTopLeft(first).dy, lessThan(tester.getTopLeft(second).dy));
  });

  testWidgets('empty state, and pull-to-refresh reloads', (tester) async {
    when(() => loans.getToday()).thenAnswer((_) async => const []);
    await pumpLoansScreens(tester, loans);
    expect(find.text(TodayHandoversView.empty), findsOneWidget);

    when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
    await tester.fling(find.text(TodayHandoversView.title), const Offset(0, 1200), 1000);
    await tester.pumpAndSettle();

    expect(find.text(TodayHandoversView.empty), findsNothing);
    expect(find.byType(HandoverCard), findsNWidgets(2));
    verify(() => loans.getToday()).called(2);
  });

  testWidgets('an error shows the server title and Retry reloads', (tester) async {
    when(() => loans.getToday()).thenThrow(httpError('/api/loans/today', 500, body: {'title': 'Server error'}));
    await pumpLoansScreens(tester, loans);
    expect(find.text('Server error'), findsOneWidget);

    when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();

    expect(find.byType(HandoverCard), findsNWidgets(2));
  });
}
