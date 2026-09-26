/// API base URL, set at build time: `--dart-define=API_URL=...` (plan §13, ADR-2).
///
/// The default is the Android emulator's alias for the host machine's localhost, where the
/// API listens on :5080. Cleartext HTTP to it is allowed in debug builds only (see
/// android/app/src/debug/res/xml/network_security_config.xml).
const apiUrl = String.fromEnvironment('API_URL', defaultValue: 'http://10.0.2.2:5080');
