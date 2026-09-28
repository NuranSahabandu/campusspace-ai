import 'package:campusspace_mobile/features/rooms/facilities_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/rooms_schedule.dart';
import '../../recording_adapter.dart';

void main() {
  test('getSchedule sends the campus date as yyyy-MM-dd and parses the day', () async {
    final adapter = RecordingAdapter(scheduleBusyJson);
    final dio = Dio(BaseOptions(baseUrl: 'http://api.test'))..httpClientAdapter = adapter;

    final schedule = await FacilitiesRepository(dio).getSchedule(3, DateTime.utc(2026, 9, 28));

    final uri = adapter.requests.single.uri;
    expect(uri.path, '/api/rooms/3/schedule');
    expect(uri.queryParameters, {'date': '2026-09-28'});
    expect(schedule.busy, hasLength(2));
  });
}
