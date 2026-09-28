import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/dio_client.dart';
import '../../core/api/paged_result.dart';
import 'models.dart';
import 'photo_picker.dart';

/// Component B's technician calls (/api/loans, /api/equipment-items). Errors are DioExceptions; callers turn them
/// into a Problem.
class LoansRepository {
  LoansRepository(this._dio);

  /// PageQuery's maximum. One booking has few loans, and a day's overdue list and one type's items fit in a page.
  static const maxPageSize = 100;

  final Dio _dio;

  /// UC09: today's (campus date) bookings that have equipment reserved, by start.
  Future<List<Handover>> getToday() async {
    final response = await _dio.get<List<dynamic>>('/api/loans/today');
    return [for (final h in response.data!) Handover.fromJson(h as Map<String, dynamic>)];
  }

  /// One booking's loans, open and returned, earliest due first.
  Future<List<Loan>> getBookingLoans(int bookingId) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/loans',
      queryParameters: {'bookingId': '$bookingId', 'sort': 'dueAt', 'pageSize': '$maxPageSize'},
    );
    return PagedResult.fromJson(response.data!, Loan.fromJson).items;
  }

  /// UC12: open loans past due, longest overdue first.
  Future<PagedResult<Loan>> getOverdue() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/loans',
      queryParameters: {'overdue': 'true', 'sort': 'dueAt', 'pageSize': '$maxPageSize'},
    );
    return PagedResult.fromJson(response.data!, Loan.fromJson);
  }

  Future<Loan> getLoan(int id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/loans/$id');
    return Loan.fromJson(response.data!);
  }

  /// The Available items of one type, by asset tag (a damaged item is never Available).
  Future<List<EquipmentItem>> getAvailableItems(int typeId) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/equipment-items',
      queryParameters: {
        'typeId': '$typeId',
        'status': 'Available',
        'sort': 'assetTag',
        'pageSize': '$maxPageSize',
      },
    );
    return PagedResult.fromJson(response.data!, EquipmentItem.fromJson).items;
  }

  /// UC10: hands one item over for one booking (201).
  Future<Loan> checkout({required int bookingId, required int itemId}) async {
    final response =
        await _dio.post<Map<String, dynamic>>('/api/loans/checkout', data: {'bookingId': bookingId, 'itemId': itemId});
    return Loan.fromJson(response.data!);
  }

  /// UC11: closes the loan (multipart). A blank [note] is not sent; [photo] null sends no file part.
  Future<Loan> checkIn(int loanId, {required String condition, String? note, PickedPhoto? photo}) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/loans/$loanId/checkin',
      data: checkInForm(condition: condition, note: note, photo: photo),
    );
    return Loan.fromJson(response.data!);
  }

  /// The check-in body. Dio sets the multipart content type and boundary for FormData.
  static FormData checkInForm({required String condition, String? note, PickedPhoto? photo}) {
    final trimmed = note?.trim();
    return FormData.fromMap({
      'condition': condition,
      if (trimmed != null && trimmed.isNotEmpty) 'note': trimmed,
      if (photo != null)
        'photo': MultipartFile.fromBytes(
          photo.bytes,
          filename: photo.name,
          contentType: DioMediaType.parse(CheckInRules.imageType(photo.bytes) ?? 'application/octet-stream'),
        ),
    });
  }
}

final loansRepositoryProvider = Provider<LoansRepository>((ref) => LoansRepository(ref.watch(dioProvider)));
