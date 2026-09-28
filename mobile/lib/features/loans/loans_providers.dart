import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/paged_result.dart';
import 'loans_repository.dart';
import 'models.dart';

// Riverpod 3 retries failing providers by default. These screens show the error with their own Retry button, so
// they never retry on their own.
Duration? _noRetry(int _, Object _) => null;

/// UC09: the technician's home list.
final todayHandoversProvider = FutureProvider.autoDispose<List<Handover>>(
  (ref) => ref.watch(loansRepositoryProvider).getToday(),
  retry: _noRetry,
);

/// One booking's loans (open and returned), for the handover screen.
final bookingLoansProvider = FutureProvider.autoDispose.family<List<Loan>, int>(
  (ref, bookingId) => ref.watch(loansRepositoryProvider).getBookingLoans(bookingId),
  retry: _noRetry,
);

/// The Available items of one type, for the handover picker.
final availableItemsProvider = FutureProvider.autoDispose.family<List<EquipmentItem>, int>(
  (ref, typeId) => ref.watch(loansRepositoryProvider).getAvailableItems(typeId),
  retry: _noRetry,
);

final loanProvider = FutureProvider.autoDispose.family<Loan, int>(
  (ref, id) => ref.watch(loansRepositoryProvider).getLoan(id),
  retry: _noRetry,
);

/// UC12.
final overdueLoansProvider = FutureProvider.autoDispose<PagedResult<Loan>>(
  (ref) => ref.watch(loansRepositoryProvider).getOverdue(),
  retry: _noRetry,
);

/// Reloads every list a checkout or check-in changes.
void invalidateLoanLists(ProviderContainer container, {int? bookingId, int? typeId, int? loanId}) {
  container
    ..invalidate(todayHandoversProvider)
    ..invalidate(overdueLoansProvider);
  if (bookingId != null) container.invalidate(bookingLoansProvider(bookingId));
  if (typeId != null) container.invalidate(availableItemsProvider(typeId));
  if (loanId != null) container.invalidate(loanProvider(loanId));
}
