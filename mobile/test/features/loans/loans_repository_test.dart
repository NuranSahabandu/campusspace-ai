import 'dart:typed_data';

import 'package:campusspace_mobile/features/loans/loans_repository.dart';
import 'package:campusspace_mobile/features/loans/photo_picker.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/loans.dart';
import '../../recording_adapter.dart';

final jpeg = Uint8List.fromList([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]);

void main() {
  (LoansRepository, RecordingAdapter) repository(String body, {int status = 200}) {
    final adapter = RecordingAdapter(body, status: status);
    final dio = Dio(BaseOptions(baseUrl: 'http://api.test'))..httpClientAdapter = adapter;
    return (LoansRepository(dio), adapter);
  }

  test('getToday reads /api/loans/today', () async {
    final (repo, adapter) = repository(todayJson);

    final today = await repo.getToday();

    expect(today, hasLength(2));
    expect(adapter.requests.single.uri.path, '/api/loans/today');
  });

  test('getBookingLoans filters by booking, earliest due first, one page of 100', () async {
    final (repo, adapter) = repository(bookingLoansJson);

    final loans = await repo.getBookingLoans(9);

    expect(loans, hasLength(3));
    final uri = adapter.requests.single.uri;
    expect(uri.path, '/api/loans');
    expect(uri.queryParameters, {'bookingId': '9', 'sort': 'dueAt', 'pageSize': '100'});
  });

  test('getOverdue asks for overdue loans', () async {
    final (repo, adapter) = repository(overdueJson);

    final page = await repo.getOverdue();

    expect(page.total, 1);
    expect(adapter.requests.single.uri.queryParameters, {'overdue': 'true', 'sort': 'dueAt', 'pageSize': '100'});
  });

  test('getAvailableItems asks for the Available items of one type', () async {
    final (repo, adapter) = repository(availableItemsJson);

    await repo.getAvailableItems(1);

    final uri = adapter.requests.single.uri;
    expect(uri.path, '/api/equipment-items');
    expect(uri.queryParameters, {'typeId': '1', 'status': 'Available', 'sort': 'assetTag', 'pageSize': '100'});
  });

  test('checkout POSTs the booking and item as JSON', () async {
    final (repo, adapter) = repository(openLoanJson, status: 201);

    final loan = await repo.checkout(bookingId: 9, itemId: 3);

    expect(loan.assetTag, 'EQ-MICW-003');
    final request = adapter.requests.single;
    expect((request.method, request.path), ('POST', '/api/loans/checkout'));
    expect(request.data, {'bookingId': 9, 'itemId': 3});
  });

  test('checkIn sends multipart condition, trimmed note and a JPEG photo part', () async {
    final (repo, adapter) = repository(damagedLoanJson);

    await repo.checkIn(5, condition: 'Damaged', note: '  Cracked grille ', photo: PickedPhoto(bytes: jpeg, name: 'mic.jpg'));

    final request = adapter.requests.single;
    expect((request.method, request.path), ('POST', '/api/loans/5/checkin'));
    final form = request.data as FormData;
    expect(Map.fromEntries(form.fields), {'condition': 'Damaged', 'note': 'Cracked grille'});
    final file = form.files.single;
    expect(file.key, 'photo');
    expect(file.value.filename, 'mic.jpg');
    expect(file.value.contentType.toString(), 'image/jpeg');
    expect(file.value.length, jpeg.length);
  });

  test('checkIn Good without a photo sends no file part and no blank note', () async {
    final (repo, adapter) = repository(openLoanJson);

    await repo.checkIn(6, condition: 'Good', note: '   ');

    final form = adapter.requests.single.data as FormData;
    expect(Map.fromEntries(form.fields), {'condition': 'Good'});
    expect(form.files, isEmpty);
  });
}
