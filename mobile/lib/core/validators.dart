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
