import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'models.dart';

/// The session in flutter_secure_storage (Android Keystore-backed), never SharedPreferences (§15.2).
///
/// Also keeps the token in memory so the dio interceptor can read it synchronously.
class TokenStorage {
  TokenStorage([this._store = const FlutterSecureStorage()]);

  static const _tokenKey = 'auth.token';
  static const _expiresAtKey = 'auth.expiresAt';
  static const _userKey = 'auth.user';

  final FlutterSecureStorage _store;
  String? _token;

  /// The current access token, or null when signed out.
  String? get token => _token;

  /// The stored session, or null if there is none or it cannot be read (then it is cleared).
  Future<AuthSession?> read() async {
    try {
      final token = await _store.read(key: _tokenKey);
      final expiresAt = await _store.read(key: _expiresAtKey);
      final user = await _store.read(key: _userKey);
      if (token == null || expiresAt == null || user == null) return null;
      final session = AuthSession(
        token: token,
        expiresAt: parseUtc(expiresAt),
        user: AppUser.fromJson(jsonDecode(user) as Map<String, dynamic>),
      );
      _token = token;
      return session;
    } catch (_) {
      // Corrupt entry or a Keystore reset (for example after a backup restore): start signed out.
      await clear();
      return null;
    }
  }

  Future<void> save(AuthSession session) async {
    await _store.write(key: _tokenKey, value: session.token);
    await _store.write(key: _expiresAtKey, value: session.expiresAt.toUtc().toIso8601String());
    await _store.write(key: _userKey, value: jsonEncode(session.user.toJson()));
    _token = session.token;
  }

  Future<void> clear() async {
    _token = null;
    try {
      await _store.delete(key: _tokenKey);
      await _store.delete(key: _expiresAtKey);
      await _store.delete(key: _userKey);
    } catch (_) {
      // Nothing more to do: the in-memory token is gone, so no request carries it.
    }
  }
}

final tokenStorageProvider = Provider<TokenStorage>((ref) => TokenStorage());
