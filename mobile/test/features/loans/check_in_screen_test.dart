import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:campusspace_mobile/features/loans/check_in_screen.dart';
import 'package:campusspace_mobile/features/loans/handover_screen.dart';
import 'package:campusspace_mobile/features/loans/models.dart';
import 'package:campusspace_mobile/features/loans/photo_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/loans.dart';
import '../../helpers.dart';

void main() {
  late MockLoansRepository loans;
  late FakePhotoPicker picker;

  setUpAll(() => registerFallbackValue(testJpeg));

  setUp(() {
    loans = MockLoansRepository();
    picker = FakePhotoPicker();
    when(() => loans.getLoan(6)).thenAnswer((_) async => liveOpenLoan);
    when(() => loans.getLoan(5)).thenAnswer((_) async => liveDamagedLoan);
  });

  void stubCheckIn(Future<Loan> Function() answer) => when(
    () => loans.checkIn(
      any(),
      condition: any(named: 'condition'),
      note: any(named: 'note'),
      photo: any(named: 'photo'),
    ),
  ).thenAnswer((_) => answer());

  Future<void> selectCondition(WidgetTester tester, String label) async {
    await tester.tap(find.byKey(const Key('checkIn.condition')));
    await tester.pumpAndSettle();
    await tester.tap(find.text(label).last);
    await tester.pumpAndSettle();
  }

  Future<void> submit(WidgetTester tester) async {
    await tester.tap(find.byKey(const Key('checkIn.submit')));
    await tester.pumpAndSettle();
  }

  Future<void> openCheckIn(WidgetTester tester, {String location = '/loans/6/checkin'}) =>
      pumpLoansScreens(tester, loans, picker: picker, initialLocation: location);

  testWidgets('shows the loan with the due time in campus time and Good selected', (tester) async {
    await openCheckIn(tester);

    expect(find.text('EQ-MICW-003'), findsOneWidget);
    expect(find.text('MIC-WIRELESS · Room A301'), findsOneWidget);
    expect(find.text('Due Mon 28 Sep 2026, 12:45'), findsOneWidget);
    expect(find.text('Good'), findsOneWidget);
    expect(find.text('0/1000'), findsOneWidget);
  });

  testWidgets('Damaged without a note or photo is blocked with both messages', (tester) async {
    await openCheckIn(tester);

    await selectCondition(tester, 'Damaged');
    await submit(tester);

    expect(find.text(CheckInRules.damagedNoteRequired), findsOneWidget);
    expect(find.text(CheckInRules.damagedPhotoRequired), findsOneWidget);
    verifyNever(
      () => loans.checkIn(
        any(),
        condition: any(named: 'condition'),
        note: any(named: 'note'),
        photo: any(named: 'photo'),
      ),
    );
  });

  testWidgets('Damaged with a note and a gallery photo sends all three and leaves the screen', (tester) async {
    stubCheckIn(() async => liveDamagedLoan);
    picker.next = testJpeg;
    // Open the check-in from the handover screen, so success pops back to it.
    when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
    when(() => loans.getBookingLoans(9)).thenAnswer((_) async => liveBookingLoans);
    await openCheckIn(tester, location: '/handovers/9');
    await tester.tap(find.descendant(of: find.byKey(const Key('open.6')), matching: find.text('Check in')));
    await tester.pumpAndSettle();
    expect(find.byType(CheckInForm), findsOneWidget);

    await selectCondition(tester, 'Damaged');
    await tester.enterText(find.byKey(const Key('checkIn.note')), 'Cracked grille');
    await tester.tap(find.text(CheckInForm.chooseFromGallery));
    await tester.pumpAndSettle();
    expect(picker.sources, [PhotoSource.gallery]);
    expect(find.byKey(const Key('checkIn.thumbnail')), findsOneWidget);
    await submit(tester);

    final captured = verify(
      () => loans.checkIn(
        6,
        condition: 'Damaged',
        note: 'Cracked grille',
        photo: captureAny(named: 'photo'),
      ),
    ).captured;
    expect((captured.single as PickedPhoto).bytes, testJpeg.bytes);
    expect(find.byType(CheckInForm), findsNothing);
    expect(find.byType(HandoverScreen), findsOneWidget);
    expect(find.text('Checked in EQ-MICW-003. It was sent to repair.'), findsOneWidget);
  });

  testWidgets('Good without a photo is sent with no photo', (tester) async {
    stubCheckIn(() async => liveOpenLoan);
    await openCheckIn(tester);

    await submit(tester);

    verify(() => loans.checkIn(6, condition: 'Good', note: '', photo: null)).called(1);
    expect(find.text('Checked in EQ-MICW-003'), findsOneWidget);
  });

  testWidgets('a photo that is not JPEG or PNG is refused before uploading', (tester) async {
    // A WebP from the gallery: "RIFF....WEBP".
    picker.next = PickedPhoto(bytes: Uint8List.fromList(ascii.encode('RIFF\x00\x00\x00\x00WEBPVP8 ')), name: 'a.webp');
    await openCheckIn(tester);

    await tester.tap(find.text(CheckInForm.chooseFromGallery));
    await tester.pumpAndSettle();

    expect(find.text(CheckInRules.photoNotAnImage), findsOneWidget);
    expect(find.byKey(const Key('checkIn.thumbnail')), findsNothing);
  });

  testWidgets('camera photo shows a thumbnail; Remove clears it', (tester) async {
    picker.next = testJpeg;
    await openCheckIn(tester);

    await tester.tap(find.text(CheckInForm.takePhoto));
    await tester.pumpAndSettle();
    expect(picker.sources, [PhotoSource.camera]);
    expect(find.byKey(const Key('checkIn.thumbnail')), findsOneWidget);

    await tester.tap(find.text('Remove'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('checkIn.thumbnail')), findsNothing);
    expect(find.text(CheckInForm.takePhoto), findsOneWidget);
  });

  testWidgets("the server's 400 field errors are shown on their fields", (tester) async {
    stubCheckIn(
      () async =>
          throw httpError('/api/loans/6/checkin', 400, body: jsonDecode(checkIn400Json) as Map<String, dynamic>),
    );
    await openCheckIn(tester);

    await submit(tester);

    final note = tester.widget<TextField>(
      find.descendant(of: find.byKey(const Key('checkIn.note')), matching: find.byType(TextField)),
    );
    expect(note.decoration!.errorText, 'A note is required for a damaged return.');
    expect(
      tester.widget<Text>(find.byKey(const Key('checkIn.photoError'))).data,
      'A photo is required for a damaged return.',
    );
    expect(find.byType(CheckInForm), findsOneWidget);
  });

  testWidgets('a double tap sends one request', (tester) async {
    final pending = Completer<Loan>();
    stubCheckIn(() => pending.future);
    await openCheckIn(tester);

    await tester.tap(find.byKey(const Key('checkIn.submit')));
    await tester.tap(find.byKey(const Key('checkIn.submit')), warnIfMissed: false);
    await tester.pump();
    expect(tester.widget<FilledButton>(find.byKey(const Key('checkIn.submit'))).onPressed, isNull);

    pending.complete(liveOpenLoan);
    await tester.pumpAndSettle();
    verify(
      () => loans.checkIn(
        any(),
        condition: any(named: 'condition'),
        note: any(named: 'note'),
        photo: any(named: 'photo'),
      ),
    ).called(1);
  });

  testWidgets('a 409 shows the title and reloads the loan', (tester) async {
    stubCheckIn(
      () async =>
          throw httpError('/api/loans/6/checkin', 409, body: jsonDecode(checkIn409Json) as Map<String, dynamic>),
    );
    await openCheckIn(tester);
    when(() => loans.getLoan(6)).thenAnswer((_) async => liveDamagedLoan);

    await submit(tester);

    expect(find.text('Loan is already checked in'), findsOneWidget);
    expect(find.text(ReturnedLoanView.alreadyCheckedIn), findsOneWidget);
    expect(find.byKey(const Key('checkIn.submit')), findsNothing);
  });

  testWidgets('a loan that is already checked in is read-only', (tester) async {
    await openCheckIn(tester, location: '/loans/5/checkin');

    expect(find.text(ReturnedLoanView.alreadyCheckedIn), findsOneWidget);
    expect(find.text('Condition: Damaged'), findsOneWidget);
    expect(find.text('Cracked grille'), findsOneWidget);
    expect(find.text('Photo on file'), findsOneWidget);
    expect(find.text('Mon 28 Sep 2026, 10:36 · by Sunil Jayasinghe'), findsOneWidget);
    expect(find.byType(CheckInForm), findsNothing);
    expect(find.byKey(const Key('checkIn.submit')), findsNothing);
  });
}
