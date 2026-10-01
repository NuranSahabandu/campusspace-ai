import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/router.dart';
import 'features/notifications/status_watcher.dart';

class CampusSpaceApp extends ConsumerWidget {
  const CampusSpaceApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Keeps the request status watcher alive while a requester is signed in (UC08).
    ref.watch(statusWatcherProvider);
    return MaterialApp.router(
      title: 'CampusSpace',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(colorSchemeSeed: const Color(0xFF1E5AA8)),
      darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF1E5AA8), brightness: Brightness.dark),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
