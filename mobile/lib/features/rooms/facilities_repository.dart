import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/dio_client.dart';
import '../../core/api/paged_result.dart';
import 'models.dart';
import 'room_filter.dart';

/// Component A read calls (/api/rooms, /api/buildings, /api/features). Errors are DioExceptions;
/// callers turn them into a Problem.
class FacilitiesRepository {
  FacilitiesRepository(this._dio);

  final Dio _dio;

  Future<PagedResult<Room>> getRooms(RoomFilter filter, {required int page, required int pageSize}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/rooms',
      queryParameters: {...filter.toQuery(), 'page': '$page', 'pageSize': '$pageSize'},
    );
    return PagedResult.fromJson(response.data!, Room.fromJson);
  }

  Future<Room> getRoom(int id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/rooms/$id');
    return Room.fromJson(response.data!);
  }

  Future<List<Building>> getBuildings() async {
    final response = await _dio.get<List<dynamic>>('/api/buildings');
    return [for (final b in response.data!) Building.fromJson(b as Map<String, dynamic>)];
  }

  Future<List<Feature>> getFeatures() async {
    final response = await _dio.get<List<dynamic>>('/api/features');
    return [for (final f in response.data!) Feature.fromJson(f as Map<String, dynamic>)];
  }
}

final facilitiesRepositoryProvider =
    Provider<FacilitiesRepository>((ref) => FacilitiesRepository(ref.watch(dioProvider)));
