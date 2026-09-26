import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_controller.dart';
import '../features/auth/login_screen.dart';
import '../features/auth/register_screen.dart';
import '../features/auth/models.dart';
import '../features/home/home_screen.dart';
import '../features/rooms/room_detail_screen.dart';
import '../features/rooms/rooms_screen.dart';

abstract final class AppRoutes {
  static const splash = '/splash';
  static const login = '/login';
  static const register = '/register';
  static const home = '/home';
  static const rooms = '/rooms';

  static String room(int id) => '$rooms/$id';
}

const _publicRoutes = {AppRoutes.login, AppRoutes.register};

/// Browsing rooms is for requesters (UC02); lab technicians do not book rooms.
const _requesterRoles = {Roles.student, Roles.lecturer};

bool _isRequesterOnly(String location) =>
    location == AppRoutes.rooms || location.startsWith('${AppRoutes.rooms}/');

/// Where the user may be, given the auth state. Null means "stay". An error counts as signed out.
/// Signed-in users who may not open a requester-only screen go home.
@visibleForTesting
String? authRedirect(AsyncValue<AuthState> auth, String location) {
  if (auth.isLoading && !auth.hasValue) return location == AppRoutes.splash ? null : AppRoutes.splash;
  final state = auth.value;
  if (state is! Authenticated) return _publicRoutes.contains(location) ? null : AppRoutes.login;
  if (location == AppRoutes.splash || _publicRoutes.contains(location)) return AppRoutes.home;
  if (_isRequesterOnly(location) && !_requesterRoles.contains(state.user.role)) return AppRoutes.home;
  return null;
}

final routerProvider = Provider<GoRouter>((ref) {
  // Bridges Riverpod to go_router: every auth change re-runs the redirect.
  final refresh = ValueNotifier(0);
  ref.listen(authControllerProvider, (_, _) => refresh.value++);

  final router = GoRouter(
    initialLocation: AppRoutes.splash,
    refreshListenable: refresh,
    redirect: (context, state) => authRedirect(ref.read(authControllerProvider), state.matchedLocation),
    routes: [
      GoRoute(path: AppRoutes.splash, builder: (_, _) => const SplashScreen()),
      GoRoute(path: AppRoutes.login, builder: (_, _) => const LoginScreen()),
      GoRoute(path: AppRoutes.register, builder: (_, _) => const RegisterScreen()),
      GoRoute(path: AppRoutes.home, builder: (_, _) => const HomeScreen()),
      GoRoute(
        path: AppRoutes.rooms,
        builder: (_, _) => const RoomsScreen(),
        routes: [
          GoRoute(
            path: ':id',
            builder: (_, state) => RoomDetailScreen(id: int.tryParse(state.pathParameters['id']!)),
          ),
        ],
      ),
    ],
  );
  ref.onDispose(() {
    router.dispose();
    refresh.dispose();
  });
  return router;
});

class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) =>
      const Scaffold(body: Center(child: CircularProgressIndicator()));
}
