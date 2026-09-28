import 'dart:async';
import 'dart:convert';

import 'package:campusspace_mobile/features/loans/handover_screen.dart';
import 'package:campusspace_mobile/features/loans/models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/loans.dart';
import '../../helpers.dart';

/// Booking 9 with [out] of its 2 MIC-WIRELESS out (the captured response has 1 out).
List<Handover> handoversWithOut(int out) => [
      for (final h in liveHandovers)
        h.bookingId != 9
            ? h
            : Handover(
                bookingId: h.bookingId,
                roomCode: h.roomCode,
                start: h.start,
                end: h.end,
                requesterName: h.requesterName,
                status: h.status,
                lines: [
                  for (final l in h.lines)
                    HandoverLine(
                      typeId: l.typeId,
                      typeCode: l.typeCode,
                      typeName: l.typeName,
                      reserved: l.reserved,
                      out: out,
                      returned: l.returned,
                    ),
                ],
              ),
    ];

void main() {
  late MockLoansRepository loans;

  setUp(() {
    loans = MockLoansRepository();
    when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
    when(() => loans.getBookingLoans(9)).thenAnswer((_) async => liveBookingLoans);
    when(() => loans.getAvailableItems(1)).thenAnswer((_) async => liveAvailableItems);
  });

  Future<void> pickAndConfirm(WidgetTester tester, String assetTag) async {
    await tester.tap(find.byKey(const Key('handOver.1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text(assetTag));
    await tester.pumpAndSettle();
    expect(find.text('Hand over $assetTag?'), findsOneWidget);
    await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Hand over')));
    await tester.pumpAndSettle();
  }

  testWidgets('shows the booking, its counts, open and returned loans', (tester) async {
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');

    expect(find.text('10:45–12:45 · A301'), findsOneWidget);
    expect(find.text('MIC-WIRELESS: 1 / 2 out · 2 returned'), findsOneWidget);
    expect(find.byKey(const Key('handOver.1')), findsOneWidget);

    final open = find.byKey(const Key('open.6'));
    expect(find.descendant(of: open, matching: find.text('EQ-MICW-003 · MIC-WIRELESS')), findsOneWidget);
    expect(find.descendant(of: open, matching: find.text('Check in')), findsOneWidget);
    expect(find.descendant(of: find.byKey(const Key('returned.4')), matching: find.textContaining('Good · checked in')),
        findsOneWidget);
    final damaged = find.byKey(const Key('returned.5'));
    expect(find.descendant(of: damaged, matching: find.textContaining('Damaged · checked in Mon 28 Sep 2026, 10:36')),
        findsOneWidget);
    expect(find.descendant(of: damaged, matching: find.byIcon(Icons.photo_camera_outlined)), findsOneWidget);
  });

  testWidgets('"Hand over" is hidden once every reserved item is out', (tester) async {
    when(() => loans.getToday()).thenAnswer((_) async => handoversWithOut(2));
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');

    expect(find.byKey(const Key('handOver.1')), findsNothing);
    expect(find.text('All out'), findsOneWidget);
  });

  testWidgets('the picker lists only the Available items of that type', (tester) async {
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');

    await tester.tap(find.byKey(const Key('handOver.1')));
    await tester.pumpAndSettle();

    expect(find.byType(ItemPickerSheet), findsOneWidget);
    expect(find.text('Available MIC-WIRELESS'), findsOneWidget);
    for (final tag in ['EQ-MICW-001', 'EQ-MICW-004', 'EQ-MICW-005', 'EQ-MICW-006']) {
      expect(find.descendant(of: find.byType(ItemPickerSheet), matching: find.text(tag)), findsOneWidget);
    }
    verify(() => loans.getAvailableItems(1)).called(1);
  });

  testWidgets('checkout after confirming refreshes the counts and loans', (tester) async {
    when(() => loans.checkout(bookingId: 9, itemId: 4)).thenAnswer((_) async => liveOpenLoan);
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');
    clearInteractions(loans);
    when(() => loans.getToday()).thenAnswer((_) async => handoversWithOut(2));

    await pickAndConfirm(tester, 'EQ-MICW-004');

    verify(() => loans.checkout(bookingId: 9, itemId: 4)).called(1);
    expect(find.text('Handed over EQ-MICW-004'), findsOneWidget);
    verify(() => loans.getToday()).called(1);
    verify(() => loans.getBookingLoans(9)).called(1);
    expect(find.text('MIC-WIRELESS: 2 / 2 out · 2 returned'), findsOneWidget);
    expect(find.byKey(const Key('handOver.1')), findsNothing);
  });

  testWidgets('cancelling the confirm sends nothing', (tester) async {
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');

    await tester.tap(find.byKey(const Key('handOver.1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('EQ-MICW-001'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();

    verifyNever(() => loans.checkout(bookingId: any(named: 'bookingId'), itemId: any(named: 'itemId')));
  });

  testWidgets('a 409 shows the server title exactly', (tester) async {
    when(() => loans.checkout(bookingId: 9, itemId: 5)).thenThrow(
        httpError('/api/loans/checkout', 409, body: jsonDecode(checkout409Json) as Map<String, dynamic>));
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');

    await pickAndConfirm(tester, 'EQ-MICW-005');

    expect(find.text('All 2 reserved MIC-WIRELESS are already out'), findsOneWidget);
  });

  testWidgets('the button is disabled while a checkout is running', (tester) async {
    final pending = Completer<Loan>();
    when(() => loans.checkout(bookingId: 9, itemId: 1)).thenAnswer((_) => pending.future);
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/9');

    await pickAndConfirm(tester, 'EQ-MICW-001');
    expect(tester.widget<FilledButton>(find.byKey(const Key('handOver.1'))).onPressed, isNull);

    pending.complete(liveOpenLoan);
    await tester.pumpAndSettle();
    expect(tester.widget<FilledButton>(find.byKey(const Key('handOver.1'))).onPressed, isNotNull);
  });

  testWidgets("a booking that isn't today says so", (tester) async {
    when(() => loans.getBookingLoans(99)).thenAnswer((_) async => const []);
    await pumpLoansScreens(tester, loans, initialLocation: '/handovers/99');

    expect(find.text(HandoverScreen.notToday), findsOneWidget);
  });
}
