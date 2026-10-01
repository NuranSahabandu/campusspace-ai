import 'package:flutter/foundation.dart';

/// API base URL, set at build time: `--dart-define=API_URL=...` (plan §13, ADR-2).
///
/// Debug and profile builds default to the Android emulator's alias for the host machine's localhost, where the API
/// listens on :5080; cleartext HTTP to it is allowed in debug builds only (see
/// android/app/src/debug/res/xml/network_security_config.xml). A release build has no default: it needs an https
/// URL, or [resolveApiUrl] throws (and `android/app/build.gradle.kts` already refuses to build it).
final String apiUrl = resolveApiUrl(const String.fromEnvironment('API_URL'), release: kReleaseMode);

const devApiUrl = 'http://10.0.2.2:5080';

/// The API URL for [defined] (the `API_URL` dart-define, blank when not given). Throws [StateError] in a [release]
/// build unless it is an absolute https URL with a host, so a release APK never silently talks to localhost.
String resolveApiUrl(String defined, {required bool release}) {
  final value = defined.trim();
  if (!release) return value.isEmpty ? devApiUrl : value;
  final uri = Uri.tryParse(value);
  if (uri == null || uri.scheme != 'https' || uri.host.isEmpty) {
    throw StateError('A release build needs --dart-define=API_URL=https://<api host>');
  }
  return value;
}

/// "Version 1.0.0 (1)" from pubspec's `version`, which Flutter passes to every build as the FLUTTER_BUILD_NAME and
/// FLUTTER_BUILD_NUMBER dart-defines; empty when they are absent.
String appVersionLabel({
  String name = const String.fromEnvironment('FLUTTER_BUILD_NAME'),
  String number = const String.fromEnvironment('FLUTTER_BUILD_NUMBER'),
}) {
  if (name.isEmpty) return '';
  return number.isEmpty ? 'Version $name' : 'Version $name ($number)';
}
