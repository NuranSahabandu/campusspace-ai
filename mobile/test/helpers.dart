import 'package:campusspace_mobile/app.dart';
import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/auth/auth_repository.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/auth/token_storage.dart';
import 'package:campusspace_mobile/features/rooms/facilities_repository.dart';
import 'package:campusspace_mobile/features/rooms/models.dart';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

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
