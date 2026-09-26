import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/auth/token_storage.dart';
import '../config.dart';
import 'session_expiry.dart';

const _timeout = Duration(seconds: 15);

/// The only HTTP client for the API. Features take it through their repositories.
final dioProvider = Provider<Dio>((ref) {
  final dio = Dio(BaseOptions(
    baseUrl: apiUrl,
    connectTimeout: _timeout,
    receiveTimeout: _timeout,
    sendTimeout: _timeout,
    contentType: Headers.jsonContentType,
  ));
  dio.interceptors.add(AuthInterceptor(
    token: () => ref.read(tokenStorageProvider).token,
    onUnauthorized: () => ref.read(sessionExpiryProvider).signal(),
  ));
  ref.onDispose(dio.close);
  return dio;
});

/// Adds the Bearer token, and reports a 401 so the app signs out and the router shows login.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({required this.token, required this.onUnauthorized});

  final String? Function() token;
  final void Function() onUnauthorized;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final value = token();
    if (value != null) options.headers['Authorization'] = 'Bearer $value';
    handler.next(options);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    // A 401 from login means wrong credentials, not an expired session.
    final request = err.requestOptions;
    final isLogin = request.method == 'POST' && request.path.endsWith('/api/auth/login');
    if (err.response?.statusCode == 401 && !isLogin) onUnauthorized();
    handler.next(err);
  }
}
