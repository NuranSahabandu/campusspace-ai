import 'package:campusspace_mobile/core/config.dart';
import 'package:campusspace_mobile/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('resolveApiUrl', () {
    test('a release build accepts an https URL', () {
      expect(
        resolveApiUrl('https://campusspace-api.onrender.com', release: true),
        'https://campusspace-api.onrender.com',
      );
    });

    for (final bad in [
      '',
      '   ',
      'http://campusspace-api.onrender.com',
      'http://10.0.2.2:5080',
      'campusspace-api.onrender.com',
      'https://',
    ]) {
      test('a release build refuses "$bad"', () {
        expect(() => resolveApiUrl(bad, release: true), throwsStateError);
      });
    }

    test('a debug build defaults to the emulator host', () {
      expect(resolveApiUrl('', release: false), devApiUrl);
    });

    test('a debug build keeps an explicit URL, http included', () {
      expect(resolveApiUrl('http://localhost:5080', release: false), 'http://localhost:5080');
    });
  });

  group('appVersionLabel', () {
    test('name and build number', () {
      expect(appVersionLabel(name: '1.0.0', number: '1'), 'Version 1.0.0 (1)');
    });

    test('name only', () {
      expect(appVersionLabel(name: '1.0.0', number: ''), 'Version 1.0.0');
    });

    test('empty without the defines', () {
      expect(appVersionLabel(name: '', number: ''), '');
    });
  });

  testWidgets('ConfigErrorApp explains the mis-built APK', (tester) async {
    await tester.pumpWidget(const ConfigErrorApp());
    expect(find.text(ConfigErrorApp.message), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
  });
}
