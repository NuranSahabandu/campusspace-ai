/// The API's shared list response (§9): `{ items, page, pageSize, total }`.
/// Total counts every match, not just this page.
class PagedResult<T> {
  const PagedResult({required this.items, required this.page, required this.pageSize, required this.total});

  factory PagedResult.fromJson(Map<String, dynamic> json, T Function(Map<String, dynamic>) itemFromJson) =>
      PagedResult(
        items: [for (final item in json['items'] as List) itemFromJson(item as Map<String, dynamic>)],
        page: (json['page'] as num).toInt(),
        pageSize: (json['pageSize'] as num).toInt(),
        total: (json['total'] as num).toInt(),
      );

  final List<T> items;
  final int page;
  final int pageSize;
  final int total;

  /// Whether pages after this one have more items.
  bool get hasMore => (page - 1) * pageSize + items.length < total;
}
