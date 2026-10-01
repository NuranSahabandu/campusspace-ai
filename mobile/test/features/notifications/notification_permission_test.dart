import 'package:campusspace_mobile/features/notifications/notification_permission.dart';
import 'package:campusspace_mobile/features/notifications/status_notifier.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../helpers.dart';

/// A notifier whose platform calls fail, like a device without the plugin.
class ThrowingNotifier extends FakeStatusNotifier {
  @override
  Future<bool> isEnabled() => throw StateError('platform');
}

void main() {
  group('NotificationPermission.askOnce', () {
    test('asks the first time when notifications are off, and marks it', () async {
      final notifier = FakeStatusNotifier(grant: true);
      final flags = FakePermissionFlagStore();
      await NotificationPermission(notifier, flags).askOnce();
      expect(notifier.permissionRequests, 1);
      expect(flags.asked, isTrue);
    });

    test('denied: no crash, and it never asks again', () async {
      final notifier = FakeStatusNotifier(grant: false);
      final flags = FakePermissionFlagStore();
      final permission = NotificationPermission(notifier, flags);
      await permission.askOnce();
      await permission.askOnce();
      expect(notifier.permissionRequests, 1);
      expect(notifier.enabled, isFalse);
    });

    test('already asked on this install (flag stored): no dialog', () async {
      final notifier = FakeStatusNotifier();
      await NotificationPermission(notifier, FakePermissionFlagStore(asked: true)).askOnce();
      expect(notifier.permissionRequests, 0);
    });

    test('already enabled (Android 12 and below, or granted in settings): no dialog', () async {
      final notifier = FakeStatusNotifier(enabled: true);
      await NotificationPermission(notifier, FakePermissionFlagStore()).askOnce();
      expect(notifier.permissionRequests, 0);
    });

    test('a platform error is swallowed', () async {
      await expectLater(NotificationPermission(ThrowingNotifier(), FakePermissionFlagStore()).askOnce(), completes);
    });
  });

  test('every notice uses the one channel, the monochrome icon and private visibility', () {
    final details = androidDetails();
    expect(details.channelId, notificationChannelId);
    expect(details.icon, notificationSmallIcon);
    expect(details.visibility, NotificationVisibility.private);
    expect(details.importance, Importance.high);
  });
}
