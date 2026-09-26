import 'package:campusspace_mobile/core/api/problem.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses a validation problem and camelCases PascalCase and \$. keys', () {
    final problem = Problem.fromJson({
      'type': 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      'title': 'One or more validation errors occurred.',
      'status': 400,
      'traceId': '00-abc-def-00',
      'errors': {
        'Email': ['The Email field is not a valid e-mail address.'],
        'FullName': ['The FullName field is required.', 'Second message.'],
        r'$.password': ['Invalid JSON.'],
      },
    });

    expect(problem.title, 'One or more validation errors occurred.');
    expect(problem.status, 400);
    expect(problem.traceId, '00-abc-def-00');
    expect(problem.fieldErrors.keys, unorderedEquals(['email', 'fullName', 'password']));
    expect(problem.fieldError('email'), 'The Email field is not a valid e-mail address.');
    expect(problem.fieldError('fullName'), 'The FullName field is required. Second message.');
    expect(problem.fieldError('role'), isNull);
  });

  test('uses the HTTP status from a DioException response', () {
    final request = RequestOptions(path: '/api/auth/login');
    final problem = Problem.fromDioException(DioException.badResponse(
      statusCode: 401,
      requestOptions: request,
      response: Response(
        requestOptions: request,
        statusCode: 401,
        data: {'title': 'Invalid email or password', 'status': 401, 'traceId': 't-1'},
      ),
    ));

    expect(problem.status, 401);
    expect(problem.title, 'Invalid email or password');
    expect(problem.traceId, 't-1');
    expect(problem.fieldErrors, isEmpty);
  });

  test('no response means the server cannot be reached', () {
    final problem = Problem.from(DioException.connectionTimeout(
      timeout: const Duration(seconds: 15),
      requestOptions: RequestOptions(path: '/api/auth/me'),
    ));

    expect(problem.title, 'Cannot reach the server');
    expect(problem.status, isNull);
  });

  test('a non-JSON body and a non-Dio error get generic titles', () {
    expect(Problem.fromJson('<html>Bad gateway</html>', status: 502).title, 'Request failed');
    expect(Problem.fromJson('<html>Bad gateway</html>', status: 502).status, 502);
    expect(Problem.from(StateError('boom')).title, 'Something went wrong');
  });
}
