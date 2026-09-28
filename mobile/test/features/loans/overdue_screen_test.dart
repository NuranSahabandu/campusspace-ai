import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/loans/models.dart';
import 'package:campusspace_mobile/features/loans/overdue_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockLoansRepository loans;

  setUp(() => loans = MockLoansRepository());

  testWidgets('lists overdue loans with the due time in campus time', (tester) async {
    when(() => loans.getOverdue()).thenAnswer((_) async => liveOverdue);
    await pumpLoansScreens(tester, loans, initialLocation: '/overdue');

    expect(find.text('EQ-MICW-007 · MIC-WIRELESS'), findsOneWidget);
    // Due 04:05:35Z is 09:35 on campus.
    expect(find.text('Room A305 · due Mon 28 Sep 2026, 09:35\nChecked out by Sunil Jayasinghe'), findsOneWidget);
    expect(find.text('Check in'), findsOneWidget);
  });

  testWidgets('pull-to-refresh reloads; empty state; a longer list says how many are shown', (tester) async {
    when(() => loans.getOverdue()).thenAnswer((_) async => const PagedResult<Loan>(items: [], page: 1, pageSize: 100, total: 0));
    await pumpLoansScreens(tester, loans, initialLocation: '/overdue');
    expect(find.text(OverdueScreen.empty), findsOneWidget);

    when(() => loans.getOverdue()).thenAnswer(
        (_) async => PagedResult(items: liveOverdue.items, page: 1, pageSize: 100, total: 101));
    await tester.fling(find.text(OverdueScreen.empty), const Offset(0, 1200), 1000);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('overdue.3')), findsOneWidget);
    expect(find.text('Showing 1 of 101'), findsOneWidget);
    verify(() => loans.getOverdue()).called(2);
  });
}
