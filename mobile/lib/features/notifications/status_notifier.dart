import 'dart:async';

import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'status_notices.dart';

/// Shows request status notices on the device. The app uses [LocalStatusNotifier]; tests override
/// [statusNotifierProvider] with a fake.
abstract interface class StatusNotifier {
  /// Shows [notice]. Never throws; does nothing when notifications are off or the permission was denied.
  Future<void> show(StatusNotice notice);

  /// Removes every notice this app has shown (on logout).
  Future<void> cancelAll();

  /// The request id of each notice the user taps while the app process runs.
  Stream<int> get taps;

  /// Whether the app may post notifications now.
  Future<bool> isEnabled();

  /// Asks for the POST_NOTIFICATIONS runtime permission (Android 13+; granted below). True when granted.
  Future<bool> requestPermission();
}

const notificationChannelId = 'request_status';
const notificationChannelName = 'Request updates';
const notificationSmallIcon = 'ic_stat_campusspace';

const _channel = AndroidNotificationChannel(
  notificationChannelId,
  notificationChannelName,
  description: 'When Facilities decides on one of your booking requests',
  importance: Importance.high,
);

/// The Android details of every notice: the one channel, the monochrome small icon, and private visibility, so a
/// secure lock screen shows only the app name and "contents hidden" (the purpose appears once unlocked).
AndroidNotificationDetails androidDetails() => const AndroidNotificationDetails(
      notificationChannelId,
      notificationChannelName,
      channelDescription: 'When Facilities decides on one of your booking requests',
      icon: notificationSmallIcon,
      importance: Importance.high,
      priority: Priority.high,
      visibility: NotificationVisibility.private,
      category: AndroidNotificationCategory.status,
    );

/// flutter_local_notifications behind [StatusNotifier]. Initialised lazily on first use (never at launch), and every
/// plugin call is guarded: a platform error or a denied permission never reaches the app.
class LocalStatusNotifier implements StatusNotifier {
  final _plugin = FlutterLocalNotificationsPlugin();
  final _taps = StreamController<int>.broadcast();
  Future<bool>? _ready;

  AndroidFlutterLocalNotificationsPlugin? get _android =>
      _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();

  Future<bool> _init() => _ready ??= _initialize();

  Future<bool> _initialize() async {
    try {
      await _plugin.initialize(
        settings: const InitializationSettings(android: AndroidInitializationSettings(notificationSmallIcon)),
        // Only while the process runs; a tap after the app was killed just opens it.
        onDidReceiveNotificationResponse: (response) {
          final id = int.tryParse(response.payload ?? '');
          if (id != null && !_taps.isClosed) _taps.add(id);
        },
      );
      await _android?.createNotificationChannel(_channel);
      return true;
    } catch (_) {
      return false;
    }
  }

  @override
  Stream<int> get taps => _taps.stream;

  @override
  Future<void> show(StatusNotice notice) async {
    if (!await _init()) return;
    final text = noticeText(notice);
    try {
      // One notice per request: a newer one replaces the older. The payload is for routing only, never shown.
      await _plugin.show(
        id: notice.requestId,
        title: text.title,
        body: text.body,
        notificationDetails: NotificationDetails(android: androidDetails()),
        payload: '${notice.requestId}',
      );
    } catch (_) {
      // Nothing to do: the request screens still show the new status.
    }
  }

  @override
  Future<void> cancelAll() async {
    if (_ready == null) return;
    try {
      await _plugin.cancelAll();
    } catch (_) {}
  }

  @override
  Future<bool> isEnabled() async {
    if (!await _init()) return false;
    try {
      return await _android?.areNotificationsEnabled() ?? false;
    } catch (_) {
      return false;
    }
  }

  @override
  Future<bool> requestPermission() async {
    if (!await _init()) return false;
    try {
      return await _android?.requestNotificationsPermission() ?? false;
    } catch (_) {
      return false;
    }
  }

  void dispose() => _taps.close();
}

final statusNotifierProvider = Provider<StatusNotifier>((ref) {
  final notifier = LocalStatusNotifier();
  ref.onDispose(notifier.dispose);
  return notifier;
});
