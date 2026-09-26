import 'package:campusspace_mobile/app.dart';
import 'package:campusspace_mobile/features/auth/auth_repository.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:campusspace_mobile/features/auth/token_storage.dart';
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
Future<void> pumpApp(WidgetTester tester, FakeTokenStorage storage, AuthRepository repository) async {
  await tester.pumpWidget(ProviderScope(
    overrides: authOverrides(storage, repository),
    child: const CampusSpaceApp(),
  ));
  await tester.pumpAndSettle();
}
