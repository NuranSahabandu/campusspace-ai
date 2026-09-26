import 'package:campusspace_mobile/features/auth/auth_controller.dart';
import 'package:campusspace_mobile/features/auth/login_screen.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/home/home_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockAuthRepository repository;
  late FakeTokenStorage storage;

  setUp(() {
    repository = MockAuthRepository();
    storage = FakeTokenStorage();
  });

  Future<void> signIn(WidgetTester tester, String email, String password) async {
    await tester.enterText(find.byKey(const Key('login.email')), email);
    await tester.enterText(find.byKey(const Key('login.password')), password);
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();
  }

  testWidgets('empty submit shows field errors and calls nothing', (tester) async {
    await pumpApp(tester, storage, repository);

    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Email is required'), findsOneWidget);
    expect(find.text('Password is required'), findsOneWidget);
    verifyNever(() => repository.login(any(), any()));
  });

  testWidgets('success navigates to the home screen', (tester) async {
    when(() => repository.login('kavindi@campusspace.local', 'CampusSpace#2026'))
        .thenAnswer((_) async => sessionFor(Roles.student));
    await pumpApp(tester, storage, repository);

    await signIn(tester, ' kavindi@campusspace.local ', 'CampusSpace#2026');

    expect(find.byType(HomeScreen), findsOneWidget);
    expect(find.text('Kavindi Perera'), findsOneWidget);
    expect(find.text('My requests'), findsOneWidget);
    expect(storage.session!.token, 'jwt-Student');
  });

  testWidgets('401 shows "Invalid email or password"', (tester) async {
    when(() => repository.login(any(), any())).thenThrow(httpError('/api/auth/login', 401,
        body: {'title': 'Invalid email or password', 'status': 401}));
    await pumpApp(tester, storage, repository);

    await signIn(tester, 'kavindi@campusspace.local', 'wrong-password');

    expect(find.text(LoginScreen.invalidCredentials), findsOneWidget);
    expect(find.byType(LoginScreen), findsOneWidget);
  });

  testWidgets('FacilitiesOfficer gets the web-portal message and no stored token', (tester) async {
    when(() => repository.login(any(), any())).thenAnswer((_) async => sessionFor(Roles.facilitiesOfficer));
    await pumpApp(tester, storage, repository);

    await signIn(tester, 'perera@campusspace.local', 'CampusSpace#2026');

    expect(find.text(WebPortalOnlyException.message), findsOneWidget);
    expect(find.byType(LoginScreen), findsOneWidget);
    expect(storage.session, isNull);
    expect(storage.token, isNull);
  });

  testWidgets('400 field errors are shown on the matching fields', (tester) async {
    when(() => repository.login(any(), any())).thenThrow(httpError('/api/auth/login', 400, body: {
      'title': 'One or more validation errors occurred.',
      'status': 400,
      'errors': {
        'Email': ['The Email field is not a valid e-mail address.'],
      },
    }));
    await pumpApp(tester, storage, repository);

    await signIn(tester, 'a@b', 'whatever');

    expect(find.text('The Email field is not a valid e-mail address.'), findsOneWidget);
  });
}
