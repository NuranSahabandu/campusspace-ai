import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/router.dart';

class CampusSpaceApp extends ConsumerWidget {
  const CampusSpaceApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      title: 'CampusSpace',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(colorSchemeSeed: const Color(0xFF1E5AA8)),
      darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF1E5AA8), brightness: Brightness.dark),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
