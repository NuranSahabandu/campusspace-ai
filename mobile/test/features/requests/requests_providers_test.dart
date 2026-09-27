import 'dart:async';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:campusspace_mobile/features/requests/requests_providers.dart';
import 'package:campusspace_mobile/features/requests/requests_repository.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../../helpers.dart';

void main() {
  late MockRequestsRepository repository;

  setUp(() => repository = MockRequestsRepository());

  ProviderContainer createContainer() {
    final container = ProviderContainer(overrides: [requestsRepositoryProvider.overrideWithValue(repository)]);
    addTearDown(container.dispose);
    // Keep the auto-dispose providers alive, as the screen would.
    container.listen(myRequestsProvider, (_, _) {});
    container.listen(requestStatusFilterProvider, (_, _) {});
    return container;
  }

  void stubPages(Future<PagedResult<RequestSummary>> Function(List<String> statuses, int page) answer) =>
      when(() => repository.getRequests(any(), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((i) => answer(i.positionalArguments[0] as List<String>, i.namedArguments[#page] as int));

  test('loads page 1, then appends page 2 until total', () async {
    stubPages((_, page) async => requestsPage(page, 25));
    final container = createContainer();

    var page = await container.read(myRequestsProvider.future);
    expect(page.items, hasLength(20));
    expect(page.hasMore, isTrue);

    await container.read(myRequestsProvider.notifier).loadMore();
    page = container.read(myRequestsProvider).requireValue;
    expect(page.items, hasLength(25));
    expect(page.hasMore, isFalse);

    await container.read(myRequestsProvider.notifier).loadMore();
    verify(() => repository.getRequests(const [], page: 2, pageSize: 20)).called(1);
    verifyNever(() => repository.getRequests(any(), page: 3, pageSize: any(named: 'pageSize')));
  });

  test('a filter change sends its statuses from page 1', () async {
    stubPages((_, page) async => requestsPage(page, 1));
    final container = createContainer();
    await container.read(myRequestsProvider.future);

    container.read(requestStatusFilterProvider.notifier).select(RequestStatusFilter.approved);
    await container.read(myRequestsProvider.future);

    verify(() => repository.getRequests(const ['Approved', 'Completed'], page: 1, pageSize: 20)).called(1);
  });

  test('a page that arrives after a filter change is dropped', () async {
    final slowPage2 = Completer<PagedResult<RequestSummary>>();
    stubPages((statuses, page) {
      if (statuses.isEmpty && page == 2) return slowPage2.future;
      if (statuses.isEmpty) return Future.value(requestsPage(page, 25));
      return Future.value(PagedResult(
        items: [testRequest(900, status: RequestStatuses.cancelled)],
        page: 1,
        pageSize: 20,
        total: 1,
      ));
    });
    final container = createContainer();
    await container.read(myRequestsProvider.future);

    final loading = container.read(myRequestsProvider.notifier).loadMore();
    container.read(requestStatusFilterProvider.notifier).select(RequestStatusFilter.closed);
    await container.read(myRequestsProvider.future);
    slowPage2.complete(requestsPage(2, 25));
    await loading;

    final page = container.read(myRequestsProvider).requireValue;
    expect(page.items.map((r) => r.id), [900]);
  });
}
