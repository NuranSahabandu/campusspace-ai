import 'package:intl/intl.dart';

// A fixed locale for the grouping and a literal prefix, so the text does not depend on the phone's language.
final _lkr = NumberFormat('#,##0.00', 'en_US');

/// An LKR amount (numeric(10,2) on the server) for display, for example "LKR 1,500.00".
/// Same output as the web's formatLkr (web/src/ui/formatLkr.ts).
String formatLkr(num amount) => 'LKR ${_lkr.format(amount)}';
