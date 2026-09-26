import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/session_expiry.dart';
import 'auth_repository.dart';
import 'models.dart';
import 'token_storage.dart';

sealed class AuthState {
  const AuthState();
}

class Anonymous extends AuthState {
  const Anonymous();
}

class Authenticated extends AuthState {
  const Authenticated(this.user);

  final AppUser user;
}

/// Thrown by login/register for roles that use the web portal. No session is stored.
class WebPortalOnlyException implements Exception {
  const WebPortalOnlyException();

  static const message =
      'This app is for students, lecturers and lab technicians. Please use the CampusSpace web portal.';

  @override
  String toString() => message;
}

/// The session (ADR-2). Loading while the stored session is checked, then Anonymous or Authenticated.
/// build() never throws, so the router only has to handle loading and data.
class AuthController extends AsyncNotifier<AuthState> {
  TokenStorage get _storage => ref.read(tokenStorageProvider);
  AuthRepository get _repository => ref.read(authRepositoryProvider);

  @override
  Future<AuthState> build() async {
    final expiry = ref.read(sessionExpiryProvider);
    expiry.addListener(_onSessionExpired);
    ref.onDispose(() => expiry.removeListener(_onSessionExpired));
    return _restore();
  }

  Future<AuthState> _restore() async {
    final stored = await _storage.read();
    if (stored == null) return const Anonymous();
    if (stored.isExpiredAt(DateTime.now().toUtc())) {
      await _storage.clear();
      return const Anonymous();
    }
    try {
      // Confirm the token still works and pick up profile or role changes.
      final user = await _repository.me();
      if (!Roles.mobile.contains(user.role)) {
        await _storage.clear();
        return const Anonymous();
      }
      await _storage.save(stored.withUser(user));
      return Authenticated(user);
    } on DioException catch (e) {
      if (e.response?.statusCode == 401) {
        await _storage.clear();
        return const Anonymous();
      }
      // Offline or server error: the token has not expired, so keep the stored session.
      return Authenticated(stored.user);
    }
  }

  Future<void> login(String email, String password) =>
      _signIn(() => _repository.login(email, password));

  Future<void> register(String fullName, String email, String password) =>
      _signIn(() => _repository.register(fullName, email, password));

  // State stays as it is while the request runs (the screen shows its own spinner), so the
  // router does not bounce to the splash screen. Errors propagate to the screen.
  Future<void> _signIn(Future<AuthSession> Function() call) async {
    final session = await call();
    if (!Roles.mobile.contains(session.user.role)) throw const WebPortalOnlyException();
    await _storage.save(session);
    state = AsyncData(Authenticated(session.user));
  }

  Future<void> logout() async {
    await _storage.clear();
    state = const AsyncData(Anonymous());
  }

  // Only a signed-in session can expire. While build() is still running, its own me() call
  // handles the 401 (and the interceptor fires this signal too), and once signed out a late
  // 401 from an in-flight request must not do anything.
  void _onSessionExpired() {
    if (state case AsyncData(value: Authenticated())) logout();
  }
}

final authControllerProvider = AsyncNotifierProvider<AuthController, AuthState>(AuthController.new);
