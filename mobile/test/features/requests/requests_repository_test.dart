import 'dart:convert';

import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:campusspace_mobile/features/requests/requests_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/requests.dart';
import '../../recording_adapter.dart';

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

  test('create POSTs the body as JSON and returns the request from the 202, already AgentProcessing', () async {
    final (repo, adapter) = repository(createdRequestJson, status: 202);

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
    expect(created.id, 31);
    expect(created.status, RequestStatuses.agentProcessing);
    expect(created.history.map((h) => h.toStatus), [RequestStatuses.submitted, RequestStatuses.agentProcessing]);
  });

  test('cancel POSTs the trimmed reason and returns the cancelled request', () async {
    final (repo, adapter) = repository(cancelledRequestJson);

    final cancelled = await repo.cancel(6, reason: '  Speaker unavailable \n');

    final sent = adapter.requests.single;
    expect(sent.method, 'POST');
    expect(sent.uri.path, '/api/booking-requests/6/cancel');
    expect(jsonDecode(jsonEncode(sent.data)), {'reason': 'Speaker unavailable'});
    expect(cancelled.status, 'Cancelled');
    expect(cancelled.cancelledAt, isNotNull);
  });

  test('cancel sends a null reason when none was typed', () async {
    final (repo, adapter) = repository(cancelledRequestJson);

    await repo.cancel(6, reason: '   ');
    await repo.cancel(6);

    for (final sent in adapter.requests) {
      expect(jsonDecode(jsonEncode(sent.data)), {'reason': null});
    }
  });
}
