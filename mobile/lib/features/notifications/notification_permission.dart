import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'status_notifier.dart';

/// Remembers that the app has asked for the notification permission on this install. Not part of the session, so
/// logout keeps it.
class PermissionFlagStore {
  PermissionFlagStore([this._store = const FlutterSecureStorage()]);

  static const _key = 'notifications.permissionAsked';

  final FlutterSecureStorage _store;

  Future<bool> wasAsked() async => await _store.read(key: _key) == 'true';

  Future<void> markAsked() => _store.write(key: _key, value: 'true');
}

final permissionFlagStoreProvider = Provider<PermissionFlagStore>((ref) => PermissionFlagStore());

/// Asks for the notification permission at most once per install, after the first successful submit (never at
/// launch). Already enabled → no dialog. Denied → nothing else happens: the app works normally without notices.
class NotificationPermission {
  NotificationPermission(this._notifier, this._flags);

  final StatusNotifier _notifier;
  final PermissionFlagStore _flags;

  /// Never throws.
  Future<void> askOnce() async {
    try {
      if (await _flags.wasAsked()) return;
      // Marked first, so a crash or a second submit during the dialog never asks again.
      await _flags.markAsked();
      if (await _notifier.isEnabled()) return;
      await _notifier.requestPermission();
    } catch (_) {
      // Storage or platform error: no notices, nothing else breaks.
    }
  }
}

final notificationPermissionProvider = Provider<NotificationPermission>(
  (ref) => NotificationPermission(ref.watch(statusNotifierProvider), ref.watch(permissionFlagStoreProvider)),
);
