import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/dio_client.dart';
import 'models.dart';

/// /api/auth calls. Errors are DioExceptions; callers turn them into a Problem.
class AuthRepository {
  AuthRepository(this._dio);

  final Dio _dio;

  Future<AuthSession> login(String email, String password) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/auth/login',
      data: {'email': email, 'password': password},
    );
    return AuthSession.fromJson(response.data!);
  }

  /// Always creates a Student (the API has no role field on register).
  Future<AuthSession> register(String fullName, String email, String password) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/auth/register',
      data: {'fullName': fullName, 'email': email, 'password': password},
    );
    return AuthSession.fromJson(response.data!);
  }

  Future<AppUser> me() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/auth/me');
    return AppUser.fromJson(response.data!);
  }
}

final authRepositoryProvider = Provider<AuthRepository>((ref) => AuthRepository(ref.watch(dioProvider)));
