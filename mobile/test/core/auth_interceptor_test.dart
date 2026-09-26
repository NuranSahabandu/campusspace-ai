import 'package:campusspace_mobile/core/api/dio_client.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Answers every request with [status] and records the request, without any network.
class _FakeAdapter implements HttpClientAdapter {
  _FakeAdapter(this.status);

  final int status;
  RequestOptions? lastRequest;

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<List<int>>? body, Future<void>? cancel) async {
    lastRequest = options;
    return ResponseBody.fromString('{}', status, headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
    });
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  late int signals;
  late String? token;

  Dio dioAnswering(_FakeAdapter adapter) => Dio(BaseOptions(baseUrl: 'http://api.test'))
    ..httpClientAdapter = adapter
    ..interceptors.add(AuthInterceptor(token: () => token, onUnauthorized: () => signals++));

  setUp(() {
    signals = 0;
    token = null;
  });

  test('adds the Bearer token when signed in', () async {
    token = 'jwt-1';
    final adapter = _FakeAdapter(200);
    await dioAnswering(adapter).get<Object>('/api/auth/me');

    expect(adapter.lastRequest!.headers['Authorization'], 'Bearer jwt-1');
  });

  test('sends no Authorization header when signed out', () async {
    final adapter = _FakeAdapter(200);
    await dioAnswering(adapter).get<Object>('/api/auth/me');

    expect(adapter.lastRequest!.headers.containsKey('Authorization'), isFalse);
  });

  test('a 401 signals session expiry', () async {
    token = 'expired';
    await expectLater(dioAnswering(_FakeAdapter(401)).get<Object>('/api/rooms'), throwsA(isA<DioException>()));

    expect(signals, 1);
  });

  test('a 401 from POST /api/auth/login does not (wrong credentials, not an expired session)', () async {
    await expectLater(
      dioAnswering(_FakeAdapter(401)).post<Object>('/api/auth/login', data: {}),
      throwsA(isA<DioException>()),
    );

    expect(signals, 0);
  });
}
