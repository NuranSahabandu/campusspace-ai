import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/problem.dart';
import '../../core/ui/error_retry_view.dart';
import 'models.dart';
import 'room_widgets.dart';
import 'rooms_providers.dart';

/// Browse rooms (plan §13, UC02): search and filter active rooms, then open one.
class RoomsScreen extends ConsumerStatefulWidget {
  const RoomsScreen({super.key});

  static const noMatches = 'No rooms match your filters';
  static const filtersFailed = "Couldn't load the filter options";

  @override
  ConsumerState<RoomsScreen> createState() => _RoomsScreenState();
}

class _RoomsScreenState extends ConsumerState<RoomsScreen> {
  // Start the next page this close to the end of the list.
  static const _loadMoreExtent = 300.0;

  final _scroll = ScrollController();
  final _search = TextEditingController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scroll.dispose();
    _search.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scroll.position.extentAfter < _loadMoreExtent) _loadMore();
  }

  Future<void> _loadMore() async {
    try {
      await ref.read(roomsListProvider.notifier).loadMore();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(Problem.from(e).title)));
    }
  }

  void _clearFilters() {
    _search.clear();
    ref.read(roomFilterProvider.notifier).clear();
  }

  List<Widget> _results(AsyncValue<RoomsPage> rooms, bool filterIsEmpty) {
    // First load, a filter change or Retry: page 1 is on its way. Pull-to-refresh keeps the list.
    if (rooms.isLoading && !(rooms.isRefreshing && rooms.hasValue)) {
      return const [SliverFillRemaining(hasScrollBody: false, child: Center(child: CircularProgressIndicator()))];
    }
    if (rooms.error case final error?) {
      return [
        SliverFillRemaining(
          hasScrollBody: false,
          child: ErrorRetryView(error: error, onRetry: () => ref.invalidate(roomsListProvider)),
        ),
      ];
    }
    final page = rooms.requireValue;
    if (page.items.isEmpty) {
      return [
        SliverFillRemaining(
          hasScrollBody: false,
          child: MessageView(
            icon: Icons.search_off,
            message: RoomsScreen.noMatches,
            action: filterIsEmpty
                ? null
                : FilledButton.tonal(onPressed: _clearFilters, child: const Text('Clear filters')),
          ),
        ),
      ];
    }
    return [
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        sliver: SliverList.builder(
          itemCount: page.items.length + (page.isLoadingMore ? 1 : 0),
          itemBuilder: (context, index) => index < page.items.length
              ? RoomCard(page.items[index])
              : const Padding(
                  key: Key('rooms.loadingMore'),
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
    final rooms = ref.watch(roomsListProvider);
    final filterIsEmpty = ref.watch(roomFilterProvider.select((f) => f.isEmpty));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Browse rooms'),
        actions: [
          if (!filterIsEmpty) TextButton(onPressed: _clearFilters, child: const Text('Clear filters')),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.read(roomsListProvider.notifier).refresh(),
        child: CustomScrollView(
          controller: _scroll,
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(child: _Filters(search: _search)),
            ..._results(rooms, filterIsEmpty),
          ],
        ),
      ),
    );
  }
}

class _Filters extends ConsumerWidget {
  const _Filters({required this.search});

  final TextEditingController search;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final filter = ref.watch(roomFilterProvider);
    final notifier = ref.read(roomFilterProvider.notifier);
    final buildingsValue = ref.watch(buildingsProvider);
    final featuresValue = ref.watch(featuresProvider);
    final buildings = buildingsValue.value ?? const [];
    final features = featuresValue.value ?? const [];
    // Rooms still load without the options; say so instead of showing empty pickers.
    final optionsError = buildingsValue.error ?? featuresValue.error;
    final optionsLoading = buildingsValue.isLoading || featuresValue.isLoading;

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextField(
            key: const Key('rooms.search'),
            controller: search,
            onChanged: notifier.setSearch,
            textInputAction: TextInputAction.search,
            decoration: const InputDecoration(
              prefixIcon: Icon(Icons.search),
              hintText: 'Search by code or name',
              border: OutlineInputBorder(),
              isDense: true,
            ),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: DropdownButton<int?>(
                  key: const Key('rooms.building'),
                  isExpanded: true,
                  value: buildings.any((b) => b.id == filter.buildingId) ? filter.buildingId : null,
                  onChanged: notifier.setBuilding,
                  items: [
                    const DropdownMenuItem(value: null, child: Text('All buildings')),
                    for (final b in buildings)
                      DropdownMenuItem(value: b.id, child: Text(b.name, overflow: TextOverflow.ellipsis)),
                  ],
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: DropdownButton<String?>(
                  key: const Key('rooms.type'),
                  isExpanded: true,
                  value: filter.type,
                  onChanged: notifier.setType,
                  items: [
                    const DropdownMenuItem(value: null, child: Text('All types')),
                    for (final t in RoomTypes.all) DropdownMenuItem(value: t, child: Text(RoomTypes.label(t))),
                  ],
                ),
              ),
            ],
          ),
          _MinCapacitySlider(value: filter.minCapacity, onChanged: notifier.setMinCapacity),
          if (optionsError != null)
            InlineLoadError(
              key: const Key('rooms.filtersError'),
              error: optionsError,
              message: '${RoomsScreen.filtersFailed}: ${Problem.from(optionsError).title}',
              onRetry: () => ref
                ..invalidate(buildingsProvider)
                ..invalidate(featuresProvider),
            )
          else if (optionsLoading && features.isEmpty)
            const LinearProgressIndicator(),
          if (features.isNotEmpty)
            Wrap(
              spacing: 8,
              runSpacing: 4,
              children: [
                for (final f in features)
                  FilterChip(
                    label: Text(f.name),
                    selected: filter.features.contains(f.code),
                    onSelected: (_) => notifier.toggleFeature(f.code),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}

/// 0–300 in steps of 5; 0 means no minimum. The filter changes when the drag ends, not on every step.
class _MinCapacitySlider extends StatefulWidget {
  const _MinCapacitySlider({required this.value, required this.onChanged});

  static const max = 300;
  static const step = 5;

  final int value;
  final ValueChanged<int> onChanged;

  @override
  State<_MinCapacitySlider> createState() => _MinCapacitySliderState();
}

class _MinCapacitySliderState extends State<_MinCapacitySlider> {
  double? _dragging;

  @override
  Widget build(BuildContext context) {
    final value = (_dragging ?? widget.value.toDouble()).clamp(0, _MinCapacitySlider.max).toDouble();
    final label = value == 0 ? 'Any' : '${value.round()}';
    return Row(
      children: [
        Text('Min capacity', style: Theme.of(context).textTheme.bodyMedium),
        Expanded(
          child: Slider(
            key: const Key('rooms.minCapacity'),
            value: value,
            max: _MinCapacitySlider.max.toDouble(),
            divisions: _MinCapacitySlider.max ~/ _MinCapacitySlider.step,
            label: label,
            onChanged: (v) => setState(() => _dragging = v),
            onChangeEnd: (v) {
              setState(() => _dragging = null);
              widget.onChanged(v.round());
            },
          ),
        ),
        SizedBox(width: 36, child: Text(label, textAlign: TextAlign.end)),
      ],
    );
  }
}

/// One room in the results: code and name, building, type, capacity and features.
class RoomCard extends StatelessWidget {
  const RoomCard(this.room, {super.key});

  final Room room;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Card(
      key: Key('room.${room.id}'),
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push('/rooms/${room.id}'),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(child: Text('${room.code} · ${room.name}', style: textTheme.titleMedium)),
                  CapacityLabel(room.capacity),
                ],
              ),
              const SizedBox(height: 4),
              Text('${room.building.name} · ${RoomTypes.label(room.type)}', style: textTheme.bodyMedium),
              if (room.features.isNotEmpty) ...[const SizedBox(height: 8), FeatureChips(room.features)],
            ],
          ),
        ),
      ),
    );
  }
}
