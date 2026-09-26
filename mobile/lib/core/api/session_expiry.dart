import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Fired by the dio interceptor when the API answers 401 (token expired or revoked).
///
/// It is a plain signal with no dependencies, so the interceptor does not depend on the auth
/// controller (which depends on dio): that would be a provider cycle. The auth controller
/// listens and decides whether to sign out.
class SessionExpiry extends ChangeNotifier {
  void signal() => notifyListeners();
}

final sessionExpiryProvider = Provider<SessionExpiry>((ref) {
  final expiry = SessionExpiry();
  ref.onDispose(expiry.dispose);
  return expiry;
});
