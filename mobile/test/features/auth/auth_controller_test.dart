import 'package:campusspace_mobile/core/api/session_expiry.dart';
import 'package:campusspace_mobile/features/auth/auth_controller.dart';
import 'package:campusspace_mobile/features/auth/models.dart';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockAuthRepository repository;

  setUp(() => repository = MockAuthRepository());

  ProviderContainer containerWith(FakeTokenStorage storage) =>
      ProviderContainer.test(overrides: authOverrides(storage, repository));

  test('no stored session → Anonymous', () async {
    final container = containerWith(FakeTokenStorage());

    expect(await container.read(authControllerProvider.future), isA<Anonymous>());
  });

  test('expired stored session is cleared on startup without calling the API', () async {
    final storage = FakeTokenStorage(sessionFor(Roles.student, validFor: const Duration(minutes: -1)));
    final container = containerWith(storage);

    expect(await container.read(authControllerProvider.future), isA<Anonymous>());
    expect(storage.session, isNull);
    verifyNever(() => repository.me());
  });

  test('valid stored session is confirmed with me() and refreshed', () async {
    final storage = FakeTokenStorage(sessionFor(Roles.student));
    final renamed = AppUser(id: 1, fullName: 'Kavindi P.', email: 'k@campusspace.local', role: Roles.student);
    when(() => repository.me()).thenAnswer((_) async => renamed);
    final container = containerWith(storage);

    final state = await container.read(authControllerProvider.future);

    expect((state as Authenticated).user.fullName, 'Kavindi P.');
    expect(storage.session!.user.fullName, 'Kavindi P.');
    expect(storage.session!.token, 'jwt-Student');
  });

  test('stored session + me() 401 → Anonymous and cleared, with no exception', () async {
    final storage = FakeTokenStorage(sessionFor(Roles.student));
    final container = containerWith(storage);
    // As in the app: the interceptor signals expiry for the 401, then dio throws.
    when(() => repository.me()).thenAnswer((_) async {
      container.read(sessionExpiryProvider).signal();
      throw httpError('/api/auth/me', 401);
    });

    final states = <AsyncValue<AuthState>>[];
    container.listen(authControllerProvider, (_, next) => states.add(next), fireImmediately: true);

    final state = await container.read(authControllerProvider.future);

    expect(state, isA<Anonymous>());
    expect(container.read(authControllerProvider), isA<AsyncData<AuthState>>());
    expect(storage.session, isNull);
    // build() handled the 401 alone: the signal did not also run logout() mid-build.
    expect(storage.clears, 1);
    expect(states, hasLength(2));
    expect(states.first.isLoading, isTrue);
  });

  test('me() unreachable keeps the unexpired stored session (offline start)', () async {
    final storage = FakeTokenStorage(sessionFor(Roles.lecturer));
    when(() => repository.me()).thenThrow(DioException.connectionError(
      requestOptions: RequestOptions(path: '/api/auth/me'),
      reason: 'offline',
    ));
    final container = containerWith(storage);

    final state = await container.read(authControllerProvider.future);

    expect((state as Authenticated).user.role, Roles.lecturer);
    expect(storage.session, isNotNull);
  });

  test('session expiry while signed in signs out', () async {
    final storage = FakeTokenStorage(sessionFor(Roles.student));
    when(() => repository.me()).thenAnswer((_) async => userWithRole(Roles.student));
    final container = containerWith(storage);
    await container.read(authControllerProvider.future);

    container.read(sessionExpiryProvider).signal();
    await pumpEventQueue();

    expect(container.read(authControllerProvider).value, isA<Anonymous>());
    expect(storage.session, isNull);
  });

  test('login as a web-portal role throws and stores nothing', () async {
    final storage = FakeTokenStorage();
    when(() => repository.login(any(), any())).thenAnswer((_) async => sessionFor(Roles.admin));
    final container = containerWith(storage);
    await container.read(authControllerProvider.future);

    await expectLater(
      container.read(authControllerProvider.notifier).login('admin@campusspace.local', 'pw'),
      throwsA(isA<WebPortalOnlyException>()),
    );
    expect(storage.session, isNull);
    expect(container.read(authControllerProvider).value, isA<Anonymous>());
  });

  test('register stores the new Student session', () async {
    final storage = FakeTokenStorage();
    when(() => repository.register(any(), any(), any())).thenAnswer((_) async => sessionFor(Roles.student));
    final container = containerWith(storage);
    await container.read(authControllerProvider.future);

    await container.read(authControllerProvider.notifier).register('New Student', 'new@campusspace.local', 'Passw0rd!');

    expect(container.read(authControllerProvider).value, isA<Authenticated>());
    expect(storage.session!.token, 'jwt-Student');
  });
}
