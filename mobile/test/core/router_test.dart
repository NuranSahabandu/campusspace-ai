import 'package:campusspace_mobile/app.dart';
import 'package:campusspace_mobile/core/router.dart';
import 'package:campusspace_mobile/features/auth/auth_controller.dart';
import 'package:campusspace_mobile/features/auth/login_screen.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/home/home_screen.dart';
import 'package:campusspace_mobile/features/loans/handovers_screen.dart';
import 'package:campusspace_mobile/features/loans/loans_repository.dart';
import 'package:campusspace_mobile/features/loans/overdue_screen.dart';
import 'package:campusspace_mobile/features/requests/my_requests_screen.dart';
import 'package:campusspace_mobile/features/requests/new_request_screen.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:campusspace_mobile/features/requests/requests_repository.dart';
import 'package:campusspace_mobile/features/rooms/facilities_repository.dart';
import 'package:campusspace_mobile/features/rooms/room_filter.dart';
import 'package:campusspace_mobile/features/rooms/rooms_screen.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers.dart';

/// The location on top of the stack (a pushed route, such as /rooms from home, is the last match).
String currentPath(WidgetTester tester) {
  final container = ProviderScope.containerOf(tester.element(find.byType(CampusSpaceApp)));
  return container.read(routerProvider).routerDelegate.currentConfiguration.last.matchedLocation;
}

void main() {
  late MockAuthRepository repository;
  late MockFacilitiesRepository facilities;
  late MockRequestsRepository requests;
  late MockLoansRepository loans;

  setUpAll(() => registerFallbackValue(const RoomFilter()));

  setUp(() {
    repository = MockAuthRepository();
    facilities = MockFacilitiesRepository();
    when(() => facilities.getRooms(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
        .thenAnswer((_) async => roomsPage(1, 2));
    when(() => facilities.getBuildings()).thenAnswer((_) async => const []);
    when(() => facilities.getFeatures()).thenAnswer((_) async => const []);
    requests = MockRequestsRepository();
    stubRequestsReferenceData(requests, facilities);
    when(() => requests.getRequest(any())).thenAnswer((_) async => lecturerRequest);
    loans = MockLoansRepository();
    when(() => loans.getToday()).thenAnswer((_) async => liveHandovers);
    when(() => loans.getOverdue()).thenAnswer((_) async => liveOverdue);
  });

  Future<void> pumpAs(WidgetTester tester, String role) async {
    when(() => repository.me()).thenAnswer((_) async => userWithRole(role));
    await pumpApp(
      tester,
      FakeTokenStorage(sessionFor(role)),
      repository,
      overrides: [
        facilitiesRepositoryProvider.overrideWithValue(facilities),
        requestsRepositoryProvider.overrideWithValue(requests),
        loansRepositoryProvider.overrideWithValue(loans),
      ],
    );
  }

  Future<void> goTo(WidgetTester tester, String location) async {
    final container = ProviderScope.containerOf(tester.element(find.byType(CampusSpaceApp)));
    container.read(routerProvider).go(location);
    await tester.pumpAndSettle();
  }

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

  testWidgets("LabTechnician lands on Today's handovers; Overdue opens; logout returns to /login", (tester) async {
    await pumpAs(tester, Roles.labTechnician);

    expect(currentPath(tester), AppRoutes.home);
    expect(find.byType(TodayHandoversView), findsOneWidget);
    expect(find.byType(HandoverCard), findsNWidgets(2));

    await tester.tap(find.byTooltip('Overdue'));
    await tester.pumpAndSettle();
    expect(currentPath(tester), AppRoutes.overdue);
    expect(find.byType(OverdueScreen), findsOneWidget);

    await goTo(tester, AppRoutes.home);
    await tester.tap(find.byTooltip('Log out'));
    await tester.pumpAndSettle();

    expect(currentPath(tester), AppRoutes.login);
  });

  for (final role in [Roles.student, Roles.lecturer]) {
    testWidgets('$role never sees the technician screens', (tester) async {
      await pumpAs(tester, role);
      expect(find.byType(TodayHandoversView), findsNothing);
      expect(find.byTooltip('Overdue'), findsNothing);

      for (final location in [AppRoutes.handover(9), AppRoutes.overdue, AppRoutes.checkIn(6)]) {
        await goTo(tester, location);
        expect(currentPath(tester), AppRoutes.home, reason: location);
      }
      verifyNever(() => loans.getToday());
      verifyNever(() => loans.getOverdue());
    });
  }

  testWidgets('LabTechnician cannot open requester screens', (tester) async {
    await pumpAs(tester, Roles.labTechnician);

    for (final location in [AppRoutes.requests, AppRoutes.newRequest, AppRoutes.request(1)]) {
      await goTo(tester, location);
      expect(currentPath(tester), AppRoutes.home, reason: location);
    }
    verifyNever(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')));
  });

  testWidgets('Student home: Browse rooms opens /rooms', (tester) async {
    await pumpAs(tester, Roles.student);

    await tester.tap(find.text('Browse rooms'));
    await tester.pumpAndSettle();

    expect(currentPath(tester), AppRoutes.rooms);
    expect(find.byType(RoomsScreen), findsOneWidget);
    expect(find.text('R1 · Room 1'), findsOneWidget);
  });

  testWidgets('Lecturer can open a room detail', (tester) async {
    when(() => facilities.getRoom(1)).thenAnswer((_) async => testRoom(1));
    await pumpAs(tester, Roles.lecturer);

    await goTo(tester, AppRoutes.room(1));

    expect(currentPath(tester), '/rooms/1');
  });

  testWidgets('LabTechnician has no Browse rooms entry and is sent from /rooms to /home', (tester) async {
    await pumpAs(tester, Roles.labTechnician);
    expect(find.text('Browse rooms'), findsNothing);

    for (final location in [AppRoutes.rooms, AppRoutes.room(1)]) {
      await goTo(tester, location);
      expect(currentPath(tester), AppRoutes.home);
      expect(find.byType(RoomsScreen), findsNothing);
    }
    verifyNever(() => facilities.getRooms(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')));
  });

  for (final role in [Roles.student, Roles.lecturer]) {
    testWidgets('$role home: New request and My requests open their screens; the detail opens', (tester) async {
      await pumpAs(tester, role);
      expect(find.text('New request'), findsOneWidget);
      expect(find.text('My requests'), findsOneWidget);

      await tester.tap(find.text('New request'));
      await tester.pumpAndSettle();
      expect(currentPath(tester), AppRoutes.newRequest);
      expect(find.byType(NewRequestScreen), findsOneWidget);

      await goTo(tester, AppRoutes.home);
      await tester.tap(find.text('My requests'));
      await tester.pumpAndSettle();
      expect(currentPath(tester), AppRoutes.requests);
      expect(find.byType(MyRequestsScreen), findsOneWidget);

      await goTo(tester, AppRoutes.request(2));
      expect(currentPath(tester), '/requests/2');
      expect(find.byType(RequestDetailScreen), findsOneWidget);
    });
  }

  testWidgets('LabTechnician has no request cards and is sent from /requests routes to /home', (tester) async {
    await pumpAs(tester, Roles.labTechnician);
    expect(find.text('New request'), findsNothing);
    expect(find.text('My requests'), findsNothing);

    for (final location in [AppRoutes.requests, AppRoutes.newRequest, AppRoutes.request(2)]) {
      await goTo(tester, location);
      expect(currentPath(tester), AppRoutes.home);
    }
    verifyNever(() => requests.getEligibility());
    verifyNever(() => requests.getRequest(any()));
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

    test('rooms are for Students and Lecturers only', () {
      AsyncData<AuthState> as(String role) => AsyncData(Authenticated(userWithRole(role)));

      for (final location in [AppRoutes.rooms, AppRoutes.room(3)]) {
        expect(authRedirect(as(Roles.student), location), isNull);
        expect(authRedirect(as(Roles.lecturer), location), isNull);
        expect(authRedirect(as(Roles.labTechnician), location), AppRoutes.home);
        expect(authRedirect(anonymous, location), AppRoutes.login);
      }
      expect(authRedirect(as(Roles.labTechnician), '/roomsx'), isNull);
    });

    test('handovers, overdue and check-in are for Lab Technicians only', () {
      AsyncData<AuthState> as(String role) => AsyncData(Authenticated(userWithRole(role)));

      for (final location in [AppRoutes.handover(9), AppRoutes.overdue, AppRoutes.checkIn(6)]) {
        expect(authRedirect(as(Roles.labTechnician), location), isNull);
        expect(authRedirect(as(Roles.student), location), AppRoutes.home);
        expect(authRedirect(as(Roles.lecturer), location), AppRoutes.home);
        expect(authRedirect(anonymous, location), AppRoutes.login);
      }
      expect(authRedirect(as(Roles.student), '/overduex'), isNull);
    });

    test('requests are for Students and Lecturers only', () {
      AsyncData<AuthState> as(String role) => AsyncData(Authenticated(userWithRole(role)));

      for (final location in [AppRoutes.requests, AppRoutes.newRequest, AppRoutes.request(3)]) {
        expect(authRedirect(as(Roles.student), location), isNull);
        expect(authRedirect(as(Roles.lecturer), location), isNull);
        expect(authRedirect(as(Roles.labTechnician), location), AppRoutes.home);
        expect(authRedirect(anonymous, location), AppRoutes.login);
      }
      expect(authRedirect(as(Roles.labTechnician), '/requestsx'), isNull);
    });
  });
}
