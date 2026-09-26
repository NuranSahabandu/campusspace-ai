import 'package:campusspace_mobile/app.dart';
import 'package:campusspace_mobile/core/router.dart';
import 'package:campusspace_mobile/features/auth/auth_controller.dart';
import 'package:campusspace_mobile/features/auth/login_screen.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/home/home_screen.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers.dart';

String currentPath(WidgetTester tester) {
  final container = ProviderScope.containerOf(tester.element(find.byType(CampusSpaceApp)));
  return container.read(routerProvider).routerDelegate.currentConfiguration.uri.path;
}

void main() {
  late MockAuthRepository repository;

  setUp(() => repository = MockAuthRepository());

  testWidgets('anonymous user is sent to /login', (tester) async {
    await pumpApp(tester, FakeTokenStorage(), repository);

    expect(currentPath(tester), AppRoutes.login);
    expect(find.byType(LoginScreen), findsOneWidget);
  });

  testWidgets('authenticated Student is sent to /home', (tester) async {
    when(() => repository.me()).thenAnswer((_) async => userWithRole(Roles.student));
    await pumpApp(tester, FakeTokenStorage(sessionFor(Roles.student)), repository);

    expect(currentPath(tester), AppRoutes.home);
    expect(find.byType(HomeScreen), findsOneWidget);
  });

  testWidgets('LabTechnician home shows the handovers placeholder; logout returns to /login', (tester) async {
    when(() => repository.me()).thenAnswer((_) async => userWithRole(Roles.labTechnician));
    await pumpApp(tester, FakeTokenStorage(sessionFor(Roles.labTechnician)), repository);

    expect(find.text("Today's handovers"), findsOneWidget);
    expect(find.text('Coming in Phase 1 (Component B)'), findsOneWidget);

    await tester.tap(find.byTooltip('Log out'));
    await tester.pumpAndSettle();

    expect(currentPath(tester), AppRoutes.login);
  });

  group('authRedirect', () {
    const loading = AsyncLoading<AuthState>();
    const anonymous = AsyncData<AuthState>(Anonymous());
    final signedIn = AsyncData<AuthState>(Authenticated(userWithRole(Roles.student)));

    test('loading → splash', () {
      expect(authRedirect(loading, AppRoutes.home), AppRoutes.splash);
      expect(authRedirect(loading, AppRoutes.splash), isNull);
    });

    test('anonymous → login, but register is allowed', () {
      expect(authRedirect(anonymous, AppRoutes.home), AppRoutes.login);
      expect(authRedirect(anonymous, AppRoutes.splash), AppRoutes.login);
      expect(authRedirect(anonymous, AppRoutes.register), isNull);
      expect(authRedirect(anonymous, AppRoutes.login), isNull);
    });

    test('signed in → home from splash, login and register', () {
      for (final location in [AppRoutes.splash, AppRoutes.login, AppRoutes.register]) {
        expect(authRedirect(signedIn, location), AppRoutes.home);
      }
      expect(authRedirect(signedIn, AppRoutes.home), isNull);
    });
  });

}
