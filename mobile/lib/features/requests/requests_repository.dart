import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/dio_client.dart';
import '../../core/api/paged_result.dart';
import 'models.dart';

/// Component C calls (/api/booking-requests) plus the reference data the New request form needs. Errors are
/// DioExceptions; callers turn them into a Problem.
class RequestsRepository {
  RequestsRepository(this._dio);

  // PageQuery's maximum page size. There are few equipment types, so one page holds them all.
  static const _equipmentTypesPageSize = 100;

  final Dio _dio;

  Future<Eligibility> getEligibility() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/booking-requests/eligibility');
    return Eligibility.fromJson(response.data!);
  }

  Future<PublicPolicy> getPolicy() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/policy-settings/public');
    return PublicPolicy.fromJson(response.data!);
  }

  Future<List<EquipmentType>> getEquipmentTypes() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/equipment-types',
      queryParameters: {'pageSize': '$_equipmentTypesPageSize', 'sort': 'code'},
    );
    return PagedResult.fromJson(response.data!, EquipmentType.fromJson).items;
  }

  /// The caller's own requests (the API only returns those), newest first. [statuses] are sent as repeated
  /// `status` parameters; empty means every status.
  Future<PagedResult<RequestSummary>> getRequests(List<String> statuses,
      {required int page, required int pageSize}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/booking-requests',
      queryParameters: {
        if (statuses.isNotEmpty) 'status': statuses,
        'page': '$page',
        'pageSize': '$pageSize',
      },
      options: Options(listFormat: ListFormat.multi),
    );
    return PagedResult.fromJson(response.data!, RequestSummary.fromJson);
  }

  Future<RequestDetail> getRequest(int id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/booking-requests/$id');
    return RequestDetail.fromJson(response.data!);
  }

  /// UC06: the request's live (Draft or Issued) .NET quotation. A 404 means it has none.
  Future<Quotation> getQuotation(int requestId) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/booking-requests/$requestId/quotation');
    return Quotation.fromJson(response.data!);
  }

  /// UC07: cancels the request and returns it. An empty [reason] is sent as null (the owner's reason is optional).
  Future<RequestDetail> cancel(int id, {String? reason}) async {
    final trimmed = reason?.trim();
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/booking-requests/$id/cancel',
      data: {'reason': (trimmed == null || trimmed.isEmpty) ? null : trimmed},
    );
    return RequestDetail.fromJson(response.data!);
  }

  /// Saves the request and starts planning it: 202 Accepted with the request, now AgentProcessing. The agent run goes
  /// on in the background (the request moves to PendingApproval or AgentFailed).
  Future<RequestDetail> create(NewRequestBody body) async {
    final response = await _dio.post<Map<String, dynamic>>('/api/booking-requests', data: body.toJson());
    return RequestDetail.fromJson(response.data!);
  }
}

final requestsRepositoryProvider =
    Provider<RequestsRepository>((ref) => RequestsRepository(ref.watch(dioProvider)));
