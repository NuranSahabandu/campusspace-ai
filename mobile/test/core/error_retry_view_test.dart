import 'package:campusspace_mobile/core/api/problem.dart';
import 'package:campusspace_mobile/core/ui/error_retry_view.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers.dart';

DioException offline(DioExceptionType type) => DioException(
  requestOptions: RequestOptions(path: '/api/rooms'),
  type: type,
  error: 'SocketException: refused',
);

void main() {
  group('Problem.offline', () {
    for (final type in [
      DioExceptionType.connectionError,
      DioExceptionType.connectionTimeout,
      DioExceptionType.receiveTimeout,
    ]) {
      test('$type has no response, so it is offline', () {
        final problem = Problem.from(offline(type));
        expect((problem.offline, problem.title), (true, 'Cannot reach the server'));
      });
    }

    test('an HTTP error is not offline', () {
      expect(Problem.from(httpError('/api/rooms', 503)).offline, isFalse);
    });
  });

  group('ErrorRetryView', () {
    Future<int Function()> pump(WidgetTester tester, Object error) async {
      var retries = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ErrorRetryView(error: error, onRetry: () => retries++),
          ),
        ),
      );
      return () => retries;
    }

    testWidgets('offline: a clear message, a hint and Retry, never exception text', (tester) async {
      final retries = await pump(tester, offline(DioExceptionType.connectionError));
      expect(find.text(ErrorRetryView.offlineTitle), findsOneWidget);
      expect(find.text(ErrorRetryView.offlineHint), findsOneWidget);
      expect(find.byIcon(Icons.cloud_off), findsOneWidget);
      expect(find.textContaining('SocketException'), findsNothing);
      await tester.tap(find.text('Retry'));
      expect(retries(), 1);
    });

    testWidgets('403: access denied without Retry', (tester) async {
      await pump(tester, httpError('/api/rooms/5', 403, body: {'title': 'Forbidden', 'status': 403}));
      expect(find.text(ErrorRetryView.forbidden), findsOneWidget);
      expect(find.text('Retry'), findsNothing);
    });

    testWidgets("another error: the server's title with Retry", (tester) async {
      await pump(tester, httpError('/api/rooms', 500, body: {'title': 'Server error'}));
      expect(find.text('Server error'), findsOneWidget);
      expect(find.text('Retry'), findsOneWidget);
    });

    testWidgets('a non-HTTP error: a generic title', (tester) async {
      await pump(tester, const FormatException('bad json'));
      expect(find.text('Something went wrong'), findsOneWidget);
      expect(find.textContaining('bad json'), findsNothing);
    });
  });
}
