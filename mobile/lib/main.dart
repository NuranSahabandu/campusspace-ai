import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app.dart';
import 'core/config.dart';

void main() {
  try {
    apiUrl; // A release build without an https API_URL stops here instead of calling localhost.
  } on StateError {
    runApp(const ConfigErrorApp());
    return;
  }
  runApp(const ProviderScope(child: CampusSpaceApp()));
}

/// Shown instead of the app when the build has no valid API_URL (a mis-built release APK).
class ConfigErrorApp extends StatelessWidget {
  const ConfigErrorApp({super.key});

  static const message = 'This build of CampusSpace has no valid API address. Please install the official APK.';

  @override
  Widget build(BuildContext context) {
    return const MaterialApp(
      debugShowCheckedModeBanner: false,
      home: Scaffold(
        body: SafeArea(
          child: Center(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: Text(message, textAlign: TextAlign.center),
            ),
          ),
        ),
      ),
    );
  }
}
