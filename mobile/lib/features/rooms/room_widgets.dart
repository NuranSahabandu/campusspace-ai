import 'package:flutter/material.dart';

import 'models.dart';

/// Small read-only chips for a room's features.
class FeatureChips extends StatelessWidget {
  const FeatureChips(this.features, {super.key});

  final List<FeatureRef> features;

  @override
  Widget build(BuildContext context) {
    final style = Theme.of(context).textTheme.labelSmall;
    return Wrap(
      spacing: 6,
      runSpacing: 6,
      children: [
        for (final f in features)
          Chip(
            label: Text(f.name, style: style),
            visualDensity: VisualDensity.compact,
            materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
            padding: EdgeInsets.zero,
          ),
      ],
    );
  }
}

/// A people icon and the room's capacity.
class CapacityLabel extends StatelessWidget {
  const CapacityLabel(this.capacity, {super.key});

  final int capacity;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.people_outline, size: 18),
          const SizedBox(width: 4),
          Text('$capacity', semanticsLabel: 'Capacity $capacity'),
        ],
      );
}

/// A centred message with an optional action, for empty and error states.
class MessageView extends StatelessWidget {
  const MessageView({super.key, required this.icon, required this.message, this.action});

  final IconData icon;
  final String message;
  final Widget? action;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 48),
              const SizedBox(height: 12),
              Text(message, textAlign: TextAlign.center, style: Theme.of(context).textTheme.titleMedium),
              if (action != null) ...[const SizedBox(height: 16), action!],
            ],
          ),
        ),
      );
}
