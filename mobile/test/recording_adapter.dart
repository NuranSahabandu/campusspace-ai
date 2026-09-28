import 'dart:typed_data';

import 'package:dio/dio.dart';

/// Answers every request with [body] and remembers what was sent, so tests see the real URL and JSON.
class RecordingAdapter implements HttpClientAdapter {
  RecordingAdapter(this.body, {this.status = 200});

  final String body;
  final int status;
  final requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    requests.add(options);
    return ResponseBody.fromString(body, status, headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
    });
  }

  @override
  void close({bool force = false}) {}
}
