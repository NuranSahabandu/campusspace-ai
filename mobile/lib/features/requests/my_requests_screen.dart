import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/campus_time.dart';
import '../../core/router.dart';
import '../rooms/room_widgets.dart';
import 'models.dart';
import 'request_status.dart';
import 'requests_providers.dart';

/// My requests (plan §13, UC06): the caller's own requests, newest first, filtered by status group.
class MyRequestsScreen extends ConsumerStatefulWidget {
  const MyRequestsScreen({super.key});

  static const empty = 'No requests yet';
  static const noMatches = 'No requests in this group';

  @override
  ConsumerState<MyRequestsScreen> createState() => _MyRequestsScreenState();
}

class _MyRequestsScreenState extends ConsumerState<MyRequestsScreen> {
  // Start the next page this close to the end of the list.
  static const _loadMoreExtent = 300.0;

  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scroll.position.extentAfter < _loadMoreExtent) _loadMore();
  }

  Future<void> _loadMore() async {
    try {
      await ref.read(myRequestsProvider.notifier).loadMore();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(Problem.from(e).title)));
    }
  }

  void _newRequest() => context.push(AppRoutes.newRequest);

  List<Widget> _results(AsyncValue<RequestsPage> requests, RequestStatusFilter filter) {
    // First load, a filter change or Retry: page 1 is on its way. Pull-to-refresh keeps the list.
    if (requests.isLoading && !(requests.isRefreshing && requests.hasValue)) {
      return const [SliverFillRemaining(hasScrollBody: false, child: Center(child: CircularProgressIndicator()))];
    }
    if (requests.error case final error?) {
      return [
        SliverFillRemaining(
          hasScrollBody: false,
          child: MessageView(
            icon: Icons.error_outline,
            message: Problem.from(error).title,
            action: FilledButton.tonal(
              onPressed: () => ref.invalidate(myRequestsProvider),
              child: const Text('Retry'),
            ),
          ),
        ),
      ];
    }
    final page = requests.requireValue;
    if (page.items.isEmpty) {
      final all = filter == RequestStatusFilter.all;
      return [
        SliverFillRemaining(
          hasScrollBody: false,
          child: MessageView(
            icon: Icons.event_note_outlined,
            message: all ? MyRequestsScreen.empty : MyRequestsScreen.noMatches,
            action: all ? FilledButton(onPressed: _newRequest, child: const Text('New request')) : null,
          ),
        ),
      ];
    }
    return [
      SliverPadding(
        // Room at the bottom for the floating button.
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 88),
        sliver: SliverList.builder(
          itemCount: page.items.length + (page.isLoadingMore ? 1 : 0),
          itemBuilder: (context, index) => index < page.items.length
              ? RequestCard(page.items[index])
              : const Padding(
                  key: Key('requests.loadingMore'),
                  padding: EdgeInsets.all(16),
                  child: Center(
                    child: SizedBox.square(dimension: 24, child: CircularProgressIndicator(strokeWidth: 2)),
                  ),
                ),
        ),
      ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final requests = ref.watch(myRequestsProvider);
    final filter = ref.watch(requestStatusFilterProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('My requests')),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('requests.new'),
        onPressed: _newRequest,
        icon: const Icon(Icons.add),
        label: const Text('New request'),
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.read(myRequestsProvider.notifier).refresh(),
        child: CustomScrollView(
          controller: _scroll,
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                child: Wrap(
                  spacing: 8,
                  children: [
                    for (final f in RequestStatusFilter.values)
                      ChoiceChip(
                        key: Key('requests.filter.${f.name}'),
                        label: Text(f.label),
                        selected: f == filter,
                        onSelected: (_) => ref.read(requestStatusFilterProvider.notifier).select(f),
                      ),
                  ],
                ),
              ),
            ),
            ..._results(requests, filter),
          ],
        ),
      ),
    );
  }
}

/// One request in the list: purpose, status, campus date and time, club or "Academic", attendees.
class RequestCard extends StatelessWidget {
  const RequestCard(this.request, {super.key});

  final RequestSummary request;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Card(
      key: Key('request.${request.id}'),
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push(AppRoutes.request(request.id)),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(child: Text(request.purpose, style: textTheme.titleMedium)),
                  const SizedBox(width: 8),
                  StatusChip(request.status),
                ],
              ),
              const SizedBox(height: 4),
              Text(formatCampusSlot(request.requestedStart, request.requestedEnd), style: textTheme.bodyMedium),
              const SizedBox(height: 2),
              Text(
                '${request.clubName ?? 'Academic'} · ${request.attendees} attendees',
                style: textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
