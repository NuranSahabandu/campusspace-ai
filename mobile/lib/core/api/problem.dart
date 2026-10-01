import 'package:dio/dio.dart';

/// RFC 9457 Problem Details, reduced to what the UI needs (mirrors web/src/api/problem.ts).
class Problem {
  const Problem({required this.title, this.status, this.traceId, this.fieldErrors = const {}, this.offline = false});

  /// Parses a Problem Details body. Anything that is not a JSON object gets a generic title.
  factory Problem.fromJson(Object? body, {int? status}) {
    final json = body is Map ? body : const {};
    final errors = json['errors'];
    return Problem(
      title: json['title'] is String ? json['title'] as String : 'Request failed',
      status: status ?? (json['status'] is int ? json['status'] as int : null),
      traceId: json['traceId'] is String ? json['traceId'] as String : null,
      fieldErrors: errors is Map
          ? {
              for (final MapEntry(:key, :value) in errors.entries)
                _toCamel('$key'): [if (value is List) ...value.map((m) => '$m') else '$value'],
            }
          : const {},
    );
  }

  factory Problem.fromDioException(DioException error) {
    final response = error.response;
    // No response at all: connection refused, DNS failure or a timeout.
    if (response == null) return const Problem(title: 'Cannot reach the server', offline: true);
    return Problem.fromJson(response.data, status: response.statusCode);
  }

  /// Any error a repository call can throw.
  factory Problem.from(Object error) => error is DioException
      ? Problem.fromDioException(error)
      : const Problem(title: 'Something went wrong');

  final String title;
  final int? status;
  final String? traceId;

  /// True when the API could not be reached (no HTTP response), as opposed to an error answer.
  final bool offline;

  /// camelCase field name → messages.
  final Map<String, List<String>> fieldErrors;

  /// The messages for [field] as one line, or null.
  String? fieldError(String field) => fieldErrors[field]?.join(' ');
}

// ASP.NET ModelState keys arrive as "Email" or "$.email"; form fields use camelCase names.
String _toCamel(String key) {
  final name = key.startsWith(r'$.') ? key.substring(2) : key;
  return name.isEmpty ? name : name[0].toLowerCase() + name.substring(1);
}
