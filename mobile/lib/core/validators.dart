// Form validators that mirror the API's data annotations (Dtos/Auth). The server is still the
// real validator; these only save a round trip. Pure functions so they can be unit-tested.

// .NET's [EmailAddress] only checks for a single '@' that is neither first nor last.
// This is slightly stricter (no whitespace) and never rejects an address the API accepts
// in practice.
final _emailPattern = RegExp(r'^[^@\s]+@[^@\s]+$');

bool _isBlank(String? value) => value == null || value.trim().isEmpty;

String? validateEmail(String? value) {
  if (_isBlank(value)) return 'Email is required';
  final email = value!.trim();
  if (email.length > 256) return 'Email must be at most 256 characters';
  if (!_emailPattern.hasMatch(email)) return 'Enter a valid email address';
  return null;
}

/// Login: [Required, MaxLength(100)]. No minimum, so old passwords still get a real answer.
String? validateLoginPassword(String? value) {
  if (_isBlank(value)) return 'Password is required';
  if (value!.length > 100) return 'Password must be at most 100 characters';
  return null;
}

/// Register: [Required, StringLength(100, MinimumLength = 8)].
String? validateNewPassword(String? value) {
  if (_isBlank(value)) return 'Password is required';
  if (value!.length < 8) return 'Password must be at least 8 characters';
  if (value.length > 100) return 'Password must be at most 100 characters';
  return null;
}

/// Register: [Required, MaxLength(100)].
String? validateFullName(String? value) {
  if (_isBlank(value)) return 'Full name is required';
  if (value!.trim().length > 100) return 'Full name must be at most 100 characters';
  return null;
}

/// Limits from the booking request DTO (backend Dtos/Requests, BookingRequestConfiguration). These are field sizes,
/// not booking policy: policy values come from GET /api/policy-settings/public.
abstract final class RequestLimits {
  static const purposeMax = 200;
  static const notesMax = 1000;
  static const minAttendees = 1;
  static const maxAttendees = 2000;
  static const maxQuantity = 50;
  static const maxBudget = 99999999.99;
}

/// [Required, MaxLength(200)]; the server also rejects a purpose that is only spaces.
String? validatePurpose(String? value) {
  if (_isBlank(value)) return 'Purpose is required';
  if (value!.length > RequestLimits.purposeMax) {
    return 'Purpose must be at most ${RequestLimits.purposeMax} characters';
  }
  return null;
}

/// [Range(1, 2000)].
String? validateAttendees(String? value) {
  if (_isBlank(value)) return 'Attendees is required';
  final count = int.tryParse(value!.trim());
  if (count == null) return 'Enter a whole number';
  if (count < RequestLimits.minAttendees || count > RequestLimits.maxAttendees) {
    return 'Attendees must be between ${RequestLimits.minAttendees} and ${RequestLimits.maxAttendees}';
  }
  return null;
}

final _amount = RegExp(r'^\d+(\.\d+)?$');
final _twoDecimals = RegExp(r'^\d+(\.\d{1,2})?$');

/// [Required, Range(0, 99999999.99)], and at most two decimal places (the service rejects a third).
String? validateBudget(String? value) {
  if (_isBlank(value)) return 'Budget is required';
  final text = value!.trim();
  if (text.startsWith('-')) return "Budget can't be negative";
  if (!_amount.hasMatch(text)) return 'Enter an amount in LKR, for example 8000';
  if (!_twoDecimals.hasMatch(text)) return 'Budget can have at most 2 decimal places.';
  if (double.parse(text) > RequestLimits.maxBudget) return 'Budget must be at most 99,999,999.99';
  return null;
}

/// The budget as sent to the API (a JSON number), or null when it is not valid.
num? parseBudget(String value) => validateBudget(value) == null ? num.parse(value.trim()) : null;

/// [MaxLength(1000)]; optional.
String? validateNotes(String? value) {
  if (value != null && value.length > RequestLimits.notesMax) {
    return 'Notes must be at most ${RequestLimits.notesMax} characters';
  }
  return null;
}
