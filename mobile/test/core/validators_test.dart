import 'package:campusspace_mobile/core/validators.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('validateEmail', () {
    test('requires a value', () {
      expect(validateEmail(null), 'Email is required');
      expect(validateEmail('   '), 'Email is required');
    });

    test('rejects addresses without a single inner @', () {
      for (final bad in ['kavindi', '@campusspace.local', 'kavindi@', 'a@b@c', 'ka vindi@x.lk']) {
        expect(validateEmail(bad), 'Enter a valid email address', reason: bad);
      }
    });

    test('accepts a valid address, ignoring surrounding spaces', () {
      expect(validateEmail('kavindi@campusspace.local'), isNull);
      expect(validateEmail('  kavindi@campusspace.local '), isNull);
    });

    test('max 256 characters', () {
      final local = 'a' * 64;
      expect(validateEmail('$local@${'b' * (256 - 65)}'), isNull);
      expect(validateEmail('$local@${'b' * (257 - 65)}'), 'Email must be at most 256 characters');
    });
  });

  group('validateLoginPassword', () {
    test('requires a value but has no minimum', () {
      expect(validateLoginPassword(''), 'Password is required');
      expect(validateLoginPassword('short'), isNull);
    });

    test('max 100 characters', () {
      expect(validateLoginPassword('a' * 100), isNull);
      expect(validateLoginPassword('a' * 101), 'Password must be at most 100 characters');
    });
  });

  group('validateNewPassword', () {
    test('8 to 100 characters', () {
      expect(validateNewPassword(null), 'Password is required');
      expect(validateNewPassword('a' * 7), 'Password must be at least 8 characters');
      expect(validateNewPassword('a' * 8), isNull);
      expect(validateNewPassword('a' * 100), isNull);
      expect(validateNewPassword('a' * 101), 'Password must be at most 100 characters');
    });
  });

  group('validateFullName', () {
    test('required, max 100 characters', () {
      expect(validateFullName(' '), 'Full name is required');
      expect(validateFullName('Kavindi Perera'), isNull);
      expect(validateFullName('a' * 100), isNull);
      expect(validateFullName('a' * 101), 'Full name must be at most 100 characters');
    });
  });

  group('booking request fields', () {
    test('purpose: required, max 200', () {
      expect(validatePurpose('  '), 'Purpose is required');
      expect(validatePurpose('a' * 200), isNull);
      expect(validatePurpose('a' * 201), 'Purpose must be at most 200 characters');
    });

    test('attendees: a whole number from 1 to 2000', () {
      expect(validateAttendees(''), 'Attendees is required');
      expect(validateAttendees('1.5'), 'Enter a whole number');
      expect(validateAttendees('0'), 'Attendees must be between 1 and 2000');
      expect(validateAttendees('1'), isNull);
      expect(validateAttendees('2000'), isNull);
      expect(validateAttendees('2001'), 'Attendees must be between 1 and 2000');
    });

    test('budget: required, not negative, at most 2 decimals and 99,999,999.99', () {
      expect(validateBudget(''), 'Budget is required');
      expect(validateBudget('-1'), "Budget can't be negative");
      expect(validateBudget('abc'), 'Enter an amount in LKR, for example 8000');
      expect(validateBudget('0'), isNull);
      expect(validateBudget('8000'), isNull);
      expect(validateBudget('8000.5'), isNull);
      expect(validateBudget('8000.55'), isNull);
      expect(validateBudget('8000.555'), 'Budget can have at most 2 decimal places.');
      expect(validateBudget('99999999.99'), isNull);
      expect(validateBudget('100000000'), 'Budget must be at most 99,999,999.99');
    });

    test('parseBudget gives a JSON number only for a valid budget', () {
      expect(parseBudget('8000'), 8000);
      expect(parseBudget(' 12.5 '), 12.5);
      expect(parseBudget('1.234'), isNull);
    });

    test('notes: optional, max 1000', () {
      expect(validateNotes(''), isNull);
      expect(validateNotes('a' * 1000), isNull);
      expect(validateNotes('a' * 1001), 'Notes must be at most 1000 characters');
    });
  });
}
