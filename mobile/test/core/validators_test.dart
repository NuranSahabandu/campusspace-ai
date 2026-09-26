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
}
