import 'dart:convert';
import 'dart:typed_data';

import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/requests_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/requests.dart';

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

void main() {
  (RequestsRepository, RecordingAdapter) repository(String body, {int status = 200}) {
    final adapter = RecordingAdapter(body, status: status);
    final dio = Dio(BaseOptions(baseUrl: 'http://api.test'))..httpClientAdapter = adapter;
    return (RequestsRepository(dio), adapter);
  }

  test('getRequests sends each status as its own parameter', () async {
    final (repo, adapter) = repository(requestsPageJson);

    await repo.getRequests(const ['Rejected', 'Cancelled', 'AgentFailed'], page: 2, pageSize: 20);

    final uri = adapter.requests.single.uri;
    expect(uri.path, '/api/booking-requests');
    expect(uri.queryParametersAll['status'], ['Rejected', 'Cancelled', 'AgentFailed']);
    expect(uri.queryParameters['page'], '2');
    expect(uri.query, contains('status=Rejected&status=Cancelled&status=AgentFailed'));
  });

  test('getRequests sends no status for All', () async {
    final (repo, adapter) = repository(requestsPageJson);

    await repo.getRequests(const [], page: 1, pageSize: 20);

    expect(adapter.requests.single.uri.queryParameters.containsKey('status'), isFalse);
  });

  test('getEquipmentTypes asks for one page of 100 sorted by code', () async {
    final (repo, adapter) = repository(equipmentTypesJson);

    final types = await repo.getEquipmentTypes();

    expect(types, hasLength(10));
    expect(adapter.requests.single.uri.queryParameters, {'pageSize': '100', 'sort': 'code'});
  });

  test('create POSTs the body as JSON and returns the saved request', () async {
    final (repo, adapter) = repository(requestDetailJson, status: 201);

    final created = await repo.create(NewRequestBody(
      purpose: 'Guest lecture',
      attendees: 120,
      date: DateTime.utc(2026, 10, 26),
      start: const TimeOfDay(hour: 10, minute: 0),
      end: const TimeOfDay(hour: 12, minute: 0),
      budgetLkr: 0,
    ));

    final sent = adapter.requests.single;
    expect(sent.method, 'POST');
    expect(sent.uri.path, '/api/booking-requests');
    expect(jsonDecode(jsonEncode(sent.data))['requestedStart'], '2026-10-26T10:00:00+05:30');
    expect(created.id, 2);
  });
}
