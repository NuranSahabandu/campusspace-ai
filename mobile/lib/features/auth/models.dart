/// Role names, mirroring backend Models/Roles.cs.
abstract final class Roles {
  static const student = 'Student';
  static const lecturer = 'Lecturer';
  static const labTechnician = 'LabTechnician';
  static const facilitiesOfficer = 'FacilitiesOfficer';
  static const admin = 'Admin';

  /// Roles that use this app. Facilities Officers and Admins use the web portal.
  static const mobile = {student, lecturer, labTechnician};

  static String label(String role) => switch (role) {
        labTechnician => 'Lab technician',
        facilitiesOfficer => 'Facilities officer',
        _ => role,
      };
}

/// The signed-in user (the API's UserDto, reduced to what the app uses).
class AppUser {
  const AppUser({required this.id, required this.fullName, required this.email, required this.role});

  factory AppUser.fromJson(Map<String, dynamic> json) => AppUser(
        id: (json['id'] as num).toInt(),
        fullName: json['fullName'] as String,
        email: json['email'] as String,
        role: json['role'] as String,
      );

  final int id;
  final String fullName;
  final String email;
  final String role;

  Map<String, dynamic> toJson() => {'id': id, 'fullName': fullName, 'email': email, 'role': role};
}

/// The API's AuthResponse: `{ accessToken, expiresAt, user }`.
class AuthSession {
  const AuthSession({required this.token, required this.expiresAt, required this.user});

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        token: json['accessToken'] as String,
        expiresAt: parseUtc(json['expiresAt'] as String),
        user: AppUser.fromJson(json['user'] as Map<String, dynamic>),
      );

  final String token;

  /// UTC.
  final DateTime expiresAt;
  final AppUser user;

  bool isExpiredAt(DateTime now) => !expiresAt.isAfter(now);

  AuthSession withUser(AppUser user) => AuthSession(token: token, expiresAt: expiresAt, user: user);
}

/// The API sends UTC; treat a timestamp without an offset as UTC rather than local time.
DateTime parseUtc(String value) {
  final parsed = DateTime.parse(value);
  return parsed.isUtc
      ? parsed
      : DateTime.utc(parsed.year, parsed.month, parsed.day, parsed.hour, parsed.minute,
          parsed.second, parsed.millisecond, parsed.microsecond);
}
