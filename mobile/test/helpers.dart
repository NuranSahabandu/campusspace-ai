import 'dart:convert';
import 'dart:typed_data';

import 'package:campusspace_mobile/app.dart';
import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/core/campus_time.dart';
import 'package:campusspace_mobile/features/auth/auth_repository.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/auth/token_storage.dart';
import 'package:campusspace_mobile/features/loans/handovers_screen.dart';
import 'package:campusspace_mobile/features/loans/loans_repository.dart';
import 'package:campusspace_mobile/features/loans/models.dart';
import 'package:campusspace_mobile/features/loans/overdue_screen.dart';
import 'package:campusspace_mobile/features/loans/photo_picker.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/my_requests_screen.dart';
import 'package:campusspace_mobile/features/requests/new_request_screen.dart';
import 'package:campusspace_mobile/features/requests/request_detail_screen.dart';
import 'package:campusspace_mobile/features/requests/requests_providers.dart';
import 'package:campusspace_mobile/features/requests/requests_repository.dart';
import 'package:campusspace_mobile/features/rooms/facilities_repository.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:campusspace_mobile/features/rooms/room_detail_screen.dart';
import 'package:campusspace_mobile/features/rooms/rooms_screen.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures/loans.dart';
import 'fixtures/requests.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

/// In-memory session store: no platform channel, and tests can inspect what was saved.
class FakeTokenStorage implements TokenStorage {
  FakeTokenStorage([this.session]);

  AuthSession? session;
  int clears = 0;

  @override
  String? get token => session?.token;

  @override
  Future<AuthSession?> read() async => session;

  @override
  Future<void> save(AuthSession value) async => session = value;

  @override
  Future<void> clear() async {
    clears++;
    session = null;
  }
}

AppUser userWithRole(String role) => AppUser(
      id: 1,
      fullName: switch (role) {
        Roles.labTechnician => 'Sunil Jayasinghe',
        Roles.facilitiesOfficer => 'Mr. Perera',
        _ => 'Kavindi Perera',
      },
      email: 'user@campusspace.local',
      role: role,
    );

AuthSession sessionFor(String role, {Duration validFor = const Duration(hours: 2)}) => AuthSession(
      token: 'jwt-$role',
      expiresAt: DateTime.now().toUtc().add(validFor),
      user: userWithRole(role),
    );

/// A DioException as dio throws it for an HTTP error with a Problem Details body.
DioException httpError(String path, int status, {Map<String, dynamic>? body}) {
  final request = RequestOptions(path: path);
  return DioException.badResponse(
    statusCode: status,
    requestOptions: request,
    response: Response(requestOptions: request, statusCode: status, data: body ?? {'status': status}),
  );
}

List<Override> authOverrides(FakeTokenStorage storage, AuthRepository repository) => [
      tokenStorageProvider.overrideWithValue(storage),
      authRepositoryProvider.overrideWithValue(repository),
    ];

/// Pumps the whole app (router included) with fake auth dependencies.
Future<void> pumpApp(WidgetTester tester, FakeTokenStorage storage, AuthRepository repository,
    {List<Override> overrides = const []}) async {
  await tester.pumpWidget(ProviderScope(
    overrides: [...authOverrides(storage, repository), ...overrides],
    child: const CampusSpaceApp(),
  ));
  await tester.pumpAndSettle();
}

class MockFacilitiesRepository extends Mock implements FacilitiesRepository {}

/// A room for tests: code R1, R2, … in building MB.
Room testRoom(int n, {List<FeatureRef> features = const []}) => Room(
      id: n,
      code: 'R$n',
      name: 'Room $n',
      type: RoomTypes.seminarRoom,
      capacity: 10 + n,
      isActive: true,
      building: const BuildingRef(id: 1, code: 'MB', name: 'Main Building'),
      features: features,
    );

/// Page [page] of [total] test rooms, [pageSize] per page.
PagedResult<Room> roomsPage(int page, int total, {int pageSize = 20}) {
  final first = (page - 1) * pageSize + 1;
  final last = (first + pageSize - 1).clamp(0, total);
  return PagedResult(
    items: [for (var n = first; n <= last; n++) testRoom(n)],
    page: page,
    pageSize: pageSize,
    total: total,
  );
}

/// Pumps [initialLocation] in a bare router with just the rooms screens (no auth), so screen
/// tests only fake the facilities repository. "Now" is [now] (default [testNow], a Monday).
Future<GoRouter> pumpRoomsScreens(WidgetTester tester, FacilitiesRepository repository,
    {String initialLocation = '/rooms', DateTime? now}) async {
  final router = GoRouter(
    initialLocation: initialLocation,
    routes: [
      GoRoute(
        path: '/rooms',
        builder: (_, _) => const RoomsScreen(),
        routes: [
          GoRoute(
            path: ':id',
            builder: (_, state) => RoomDetailScreen(id: int.tryParse(state.pathParameters['id']!)),
          ),
        ],
      ),
    ],
  );
  addTearDown(router.dispose);
  await tester.pumpWidget(ProviderScope(
    overrides: [
      facilitiesRepositoryProvider.overrideWithValue(repository),
      clockProvider.overrideWithValue(() => now ?? testNow),
    ],
    child: MaterialApp.router(routerConfig: router),
  ));
  await tester.pumpAndSettle();
  return router;
}

class MockRequestsRepository extends Mock implements RequestsRepository {}

Map<String, dynamic> _json(String text) => jsonDecode(text) as Map<String, dynamic>;

/// The captured API responses as models.
final studentEligibility = Eligibility.fromJson(_json(eligibilityStudentJson));
final notRepEligibility = Eligibility.fromJson(_json(eligibilityNotRepJson));
final lecturerEligibility = Eligibility.fromJson(_json(eligibilityLecturerJson));
final livePolicy = PublicPolicy.fromJson(_json(policyJson));
final liveEquipmentTypes = PagedResult.fromJson(_json(equipmentTypesJson), EquipmentType.fromJson).items;
final lecturerRequest = RequestDetail.fromJson(_json(requestDetailJson));

/// Monday 28 Sep 2026, 10:00 campus time: "now" in the requests screen tests.
final testNow = campusInstant(DateTime.utc(2026, 9, 28), const TimeOfDay(hour: 10, minute: 0));

/// A request summary for tests: purpose "Request N", Tue 20 Oct 2026 14:00–17:00 campus.
RequestSummary testRequest(int n, {String status = 'Submitted', String? clubName = 'Robotics Club'}) => RequestSummary(
      id: n,
      purpose: 'Request $n',
      status: status,
      requestedStart: DateTime.utc(2026, 10, 20, 8, 30),
      requestedEnd: DateTime.utc(2026, 10, 20, 11, 30),
      attendees: 40 + n,
      budgetLkr: 8000,
      clubName: clubName,
      requesterName: 'Kavindi Perera',
      createdAt: DateTime.utc(2026, 9, 27, 10),
    );

/// Page [page] of [total] test requests, [pageSize] per page.
PagedResult<RequestSummary> requestsPage(int page, int total, {int pageSize = 20}) {
  final first = (page - 1) * pageSize + 1;
  final last = (first + pageSize - 1).clamp(0, total);
  return PagedResult(
    items: [for (var n = first; n <= last; n++) testRequest(n)],
    page: page,
    pageSize: pageSize,
    total: total,
  );
}

/// Stubs the reference data every requests screen may load: the given eligibility, the live policy, equipment
/// types and features, and an empty request list.
void stubRequestsReferenceData(MockRequestsRepository requests, MockFacilitiesRepository facilities,
    {Eligibility? eligibility}) {
  when(() => requests.getEligibility()).thenAnswer((_) async => eligibility ?? studentEligibility);
  when(() => requests.getPolicy()).thenAnswer((_) async => livePolicy);
  when(() => requests.getEquipmentTypes()).thenAnswer((_) async => liveEquipmentTypes);
  when(() => requests.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
      .thenAnswer((_) async => requestsPage(1, 0));
  when(() => facilities.getFeatures()).thenAnswer((_) async => const [
        Feature(id: 1, code: 'projector', name: 'Projector'),
        Feature(id: 2, code: 'computers', name: 'Computers'),
      ]);
}

/// Kavindi's id in the seeded database: the default signed-in user in the requests screen tests.
const kavindiId = 1;

/// Pumps [initialLocation] in a bare router with just the requests screens (no auth), as user [userId] with [role]
/// at [now] (default [testNow]). A tall surface keeps every stepper step on screen.
Future<GoRouter> pumpRequestsScreens(
  WidgetTester tester,
  RequestsRepository requests, {
  FacilitiesRepository? facilities,
  String initialLocation = '/requests',
  String role = Roles.student,
  int userId = kavindiId,
  DateTime? now,
}) async {
  tester.view.physicalSize = const Size(900, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final router = GoRouter(
    initialLocation: initialLocation,
    routes: [
      GoRoute(
        path: '/requests',
        builder: (_, _) => const MyRequestsScreen(),
        routes: [
          GoRoute(path: 'new', builder: (_, _) => const NewRequestScreen()),
          GoRoute(
            path: ':id',
            builder: (_, state) => RequestDetailScreen(id: int.tryParse(state.pathParameters['id']!)),
          ),
        ],
      ),
    ],
  );
  addTearDown(router.dispose);
  await tester.pumpWidget(ProviderScope(
    overrides: [
      requestsRepositoryProvider.overrideWithValue(requests),
      if (facilities != null) facilitiesRepositoryProvider.overrideWithValue(facilities),
      requesterRoleProvider.overrideWithValue(role),
      currentUserIdProvider.overrideWithValue(userId),
      clockProvider.overrideWithValue(() => now ?? testNow),
    ],
    child: MaterialApp.router(routerConfig: router),
  ));
  await tester.pumpAndSettle();
  return router;
}

class MockLoansRepository extends Mock implements LoansRepository {}

/// Hands back [next] (or null: the user backed out) and remembers each source asked for.
class FakePhotoPicker implements PhotoPicker {
  FakePhotoPicker([this.next]);

  PickedPhoto? next;
  final sources = <PhotoSource>[];

  @override
  Future<PickedPhoto?> pick(PhotoSource source) async {
    sources.add(source);
    return next;
  }
}

/// The first bytes of a JPEG: enough for the client and server magic-byte checks.
final testJpeg = PickedPhoto(bytes: Uint8List.fromList([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]), name: 'mic.jpg');

/// The captured loan API responses as models.
final liveHandovers = [for (final h in jsonDecode(todayJson) as List) Handover.fromJson(h as Map<String, dynamic>)];
final liveBookingLoans = PagedResult.fromJson(_json(bookingLoansJson), Loan.fromJson).items;
final liveOverdue = PagedResult.fromJson(_json(overdueJson), Loan.fromJson);
final liveAvailableItems = PagedResult.fromJson(_json(availableItemsJson), EquipmentItem.fromJson).items;
final liveOpenLoan = Loan.fromJson(_json(openLoanJson));
final liveDamagedLoan = Loan.fromJson(_json(damagedLoanJson));

/// Monday 28 Sep 2026, 10:40 campus time: "now" in the loans screen tests (booking 9 starts at 10:45).
final loansNow = DateTime.utc(2026, 9, 28, 5, 10);

/// Pumps [initialLocation] in a bare router with just the technician screens (no auth): /home is today's handovers.
Future<GoRouter> pumpLoansScreens(
  WidgetTester tester,
  LoansRepository loans, {
  PhotoPicker? picker,
  String initialLocation = '/home',
}) async {
  tester.view.physicalSize = const Size(900, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final router = GoRouter(
    initialLocation: initialLocation,
    routes: [
      GoRoute(path: '/home', builder: (_, _) => const Scaffold(body: TodayHandoversView())),
      GoRoute(path: '/overdue', builder: (_, _) => const OverdueScreen()),
    ],
  );
  addTearDown(router.dispose);
  await tester.pumpWidget(ProviderScope(
    overrides: [
      loansRepositoryProvider.overrideWithValue(loans),
      photoPickerProvider.overrideWithValue(picker ?? FakePhotoPicker()),
      clockProvider.overrideWithValue(() => loansNow),
    ],
    child: MaterialApp.router(routerConfig: router),
  ));
  await tester.pumpAndSettle();
  return router;
}
