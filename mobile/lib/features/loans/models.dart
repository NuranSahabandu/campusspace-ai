// The API's loan DTOs (backend Dtos/Loans/LoanDtos.cs) and equipment items (Dtos/Equipment/EquipmentItemDtos.cs).
// Instants arrive as UTC ("...Z"); screens show them in campus time (core/campus_time.dart).

DateTime _instant(Object? value) => DateTime.parse(value as String).toUtc();

DateTime? _optionalInstant(Object? value) => value == null ? null : _instant(value);

int _int(Object? value) => (value as num).toInt();

/// Return conditions (EquipmentConditions on the server).
abstract final class ItemConditions {
  static const good = 'Good';
  static const minorWear = 'MinorWear';
  static const damaged = 'Damaged';

  static const all = [good, minorWear, damaged];

  static String label(String condition) => switch (condition) {
        minorWear => 'Minor wear',
        _ => condition,
      };
}

/// The check-in rules the server enforces (LoanService, DamagePhotoStore, CheckInRequest), mirrored to save a round
/// trip. The server still decides.
abstract final class CheckInRules {
  /// EquipmentLoanConfiguration.DamageNoteMaxLength.
  static const noteMaxLength = 1000;

  /// DamagePhotoStore.MaxBytes (the §15.3 upload limit).
  static const photoMaxBytes = 5 * 1024 * 1024;

  static const damagedNoteRequired = 'A note is required for a damaged return.';
  static const damagedPhotoRequired = 'A photo is required for a damaged return.';
  static const photoTooLarge = 'Photo must be 5 MB or smaller.';
  static const photoNotAnImage = 'Photo must be a JPEG or PNG image.';

  static const _jpegMagic = [0xFF, 0xD8, 0xFF];
  static const _pngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  static bool _startsWith(List<int> bytes, List<int> magic) {
    if (bytes.length < magic.length) return false;
    for (var i = 0; i < magic.length; i++) {
      if (bytes[i] != magic[i]) return false;
    }
    return true;
  }

  /// "image/png" or "image/jpeg" from the first bytes (as DamagePhotoStore.Detect), or null for anything else
  /// (for example HEIC or WebP from the gallery).
  static String? imageType(List<int> bytes) => _startsWith(bytes, _pngMagic)
      ? 'image/png'
      : _startsWith(bytes, _jpegMagic)
          ? 'image/jpeg'
          : null;

  /// The photo's error, or null when it may be uploaded.
  static String? photoError(List<int> bytes) {
    if (bytes.length > photoMaxBytes) return photoTooLarge;
    if (imageType(bytes) == null) return photoNotAnImage;
    return null;
  }
}

/// One reserved type of a handover (HandoverLineDto): reserved, out now, and already returned.
class HandoverLine {
  const HandoverLine({
    required this.typeId,
    required this.typeCode,
    required this.typeName,
    required this.reserved,
    required this.out,
    required this.returned,
  });

  factory HandoverLine.fromJson(Map<String, dynamic> json) => HandoverLine(
        typeId: _int(json['typeId']),
        typeCode: json['typeCode'] as String,
        typeName: json['typeName'] as String,
        reserved: _int(json['reserved']),
        out: _int(json['out']),
        returned: _int(json['returned']),
      );

  final int typeId;
  final String typeCode;
  final String typeName;
  final int reserved;
  final int out;
  final int returned;

  /// The server refuses a checkout once every reserved item is out.
  bool get canHandOver => out < reserved;
}

/// GET /api/loans/today: an active booking of today (campus date) that has equipment reserved (HandoverDto).
class Handover {
  const Handover({
    required this.bookingId,
    required this.roomCode,
    required this.start,
    required this.end,
    required this.requesterName,
    required this.status,
    required this.lines,
  });

  factory Handover.fromJson(Map<String, dynamic> json) => Handover(
        bookingId: _int(json['bookingId']),
        roomCode: json['roomCode'] as String,
        start: _instant(json['start']),
        end: _instant(json['end']),
        requesterName: json['requesterName'] as String,
        status: json['status'] as String,
        lines: [for (final l in json['lines'] as List) HandoverLine.fromJson(l as Map<String, dynamic>)],
      );

  final int bookingId;
  final String roomCode;
  final DateTime start;
  final DateTime end;
  final String requesterName;
  final String status;
  final List<HandoverLine> lines;
}

/// One loan (LoanDto). Open while [checkedInAt] is null; [isOverdue] is the server's "open and past due".
class Loan {
  const Loan({
    required this.id,
    required this.bookingId,
    required this.roomCode,
    required this.itemId,
    required this.assetTag,
    required this.typeCode,
    required this.checkedOutAt,
    required this.checkedOutByName,
    required this.dueAt,
    this.checkedInAt,
    this.checkedInByName,
    this.returnCondition,
    this.damageNote,
    this.isLateReturn = false,
    this.isOverdue = false,
    this.hasPhoto = false,
  });

  factory Loan.fromJson(Map<String, dynamic> json) => Loan(
        id: _int(json['id']),
        bookingId: _int(json['bookingId']),
        roomCode: json['roomCode'] as String,
        itemId: _int(json['itemId']),
        assetTag: json['assetTag'] as String,
        typeCode: json['typeCode'] as String,
        checkedOutAt: _instant(json['checkedOutAt']),
        checkedOutByName: json['checkedOutByName'] as String,
        dueAt: _instant(json['dueAt']),
        checkedInAt: _optionalInstant(json['checkedInAt']),
        checkedInByName: json['checkedInByName'] as String?,
        returnCondition: json['returnCondition'] as String?,
        damageNote: json['damageNote'] as String?,
        isLateReturn: json['isLateReturn'] as bool,
        isOverdue: json['isOverdue'] as bool,
        hasPhoto: json['hasPhoto'] as bool,
      );

  final int id;
  final int bookingId;
  final String roomCode;
  final int itemId;
  final String assetTag;
  final String typeCode;
  final DateTime checkedOutAt;
  final String checkedOutByName;
  final DateTime dueAt;
  final DateTime? checkedInAt;
  final String? checkedInByName;
  final String? returnCondition;
  final String? damageNote;
  final bool isLateReturn;
  final bool isOverdue;
  final bool hasPhoto;

  bool get isOpen => checkedInAt == null;
}

/// An equipment item as the handover picker needs it (EquipmentItemDto).
class EquipmentItem {
  const EquipmentItem({
    required this.id,
    required this.assetTag,
    required this.typeId,
    required this.typeCode,
    required this.condition,
    required this.status,
  });

  factory EquipmentItem.fromJson(Map<String, dynamic> json) => EquipmentItem(
        id: _int(json['id']),
        assetTag: json['assetTag'] as String,
        typeId: _int(json['typeId']),
        typeCode: json['typeCode'] as String,
        condition: json['condition'] as String,
        status: json['status'] as String,
      );

  final int id;
  final String assetTag;
  final int typeId;
  final String typeCode;
  final String condition;
  final String status;
}
