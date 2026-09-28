import 'dart:convert';

import 'package:campusspace_mobile/core/api/paged_result.dart';
import 'package:campusspace_mobile/features/loans/models.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/loans.dart';

Map<String, dynamic> _json(String text) => jsonDecode(text) as Map<String, dynamic>;

void main() {
  test('Handover parses GET /api/loans/today', () {
    final list = [for (final h in jsonDecode(todayJson) as List) Handover.fromJson(h as Map<String, dynamic>)];

    expect(list.map((h) => h.bookingId), [10, 9]);
    final booking = list[1];
    expect(booking.roomCode, 'A301');
    expect(booking.requesterName, 'Dr. Nimal Fernando');
    expect(booking.start.isUtc, isTrue);
    expect(booking.start, DateTime.utc(2026, 9, 28, 5, 15, 35, 507, 13));
    final line = booking.lines.single;
    expect((line.typeId, line.typeCode, line.reserved, line.out, line.returned), (1, 'MIC-WIRELESS', 2, 1, 2));
    expect(line.canHandOver, isTrue);
    expect(list[0].lines.single.canHandOver, isFalse);
  });

  test('Loan parses open, returned and overdue loans', () {
    final loans = PagedResult.fromJson(_json(bookingLoansJson), Loan.fromJson).items;

    expect(loans.map((l) => l.assetTag), ['EQ-MICW-001', 'EQ-MICW-002', 'EQ-MICW-003']);
    final damaged = loans[1];
    expect(damaged.isOpen, isFalse);
    expect(damaged.returnCondition, 'Damaged');
    expect(damaged.damageNote, 'Cracked grille');
    expect(damaged.hasPhoto, isTrue);
    expect(loans[2].isOpen, isTrue);

    final overdue = PagedResult.fromJson(_json(overdueJson), Loan.fromJson).items.single;
    expect(overdue.isOverdue, isTrue);
    expect(overdue.checkedOutByName, 'Sunil Jayasinghe');
    expect(overdue.dueAt.isUtc, isTrue);
  });

  test('EquipmentItem parses the Available items of a type', () {
    final items = PagedResult.fromJson(_json(availableItemsJson), EquipmentItem.fromJson).items;

    expect(items.map((i) => i.assetTag), ['EQ-MICW-001', 'EQ-MICW-004', 'EQ-MICW-005', 'EQ-MICW-006']);
    expect(items.every((i) => i.status == 'Available' && i.typeId == 1), isTrue);
  });

  group('CheckInRules.photoError mirrors DamagePhotoStore', () {
    test('accepts JPEG and PNG by their first bytes', () {
      expect(CheckInRules.photoError([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]), isNull);
      expect(CheckInRules.photoError([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]), isNull);
      expect(CheckInRules.imageType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), 'image/png');
    });

    test('refuses other formats, empty and too-large files', () {
      // A WebP ("RIFF....WEBP") and a HEIC ("....ftypheic") from the gallery.
      expect(CheckInRules.photoError(ascii.encode('RIFF\x00\x00\x00\x00WEBPVP8 ')), CheckInRules.photoNotAnImage);
      expect(CheckInRules.photoError([0, 0, 0, 0x18, ...ascii.encode('ftypheic')]), CheckInRules.photoNotAnImage);
      expect(CheckInRules.photoError(const []), CheckInRules.photoNotAnImage);
      final big = List<int>.filled(CheckInRules.photoMaxBytes + 1, 0)..setAll(0, [0xFF, 0xD8, 0xFF]);
      expect(CheckInRules.photoError(big), CheckInRules.photoTooLarge);
    });
  });
}
