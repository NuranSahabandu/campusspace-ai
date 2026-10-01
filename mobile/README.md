# mobile

Flutter (Android only) app for requesters (Students, Lecturers) and Lab Technicians (plan §13, ADR-2).
Shell owned by Member 3 (C); each component adds its screens under `lib/features/<area>/`.

## Run

```bash
flutter pub get
flutter emulators --launch Pixel_10
flutter run -d <emulator-id> --dart-define=API_URL=http://10.0.2.2:5080
flutter analyze
flutter test
```

`API_URL` defaults to `http://10.0.2.2:5080` (`lib/core/config.dart`): the Android emulator's alias for the host's
`localhost`, where the API listens. On a physical phone, pass your machine's LAN address instead (cleartext to other
hosts is blocked, see below).

## Structure

```
lib/
  main.dart, app.dart          ProviderScope + MaterialApp.router
  core/
    config.dart                API_URL (--dart-define)
    validators.dart            pure validators mirroring the API DTO annotations
    router.dart                GoRouter; redirect: loading → /splash, anonymous → /login (or /register), signed in → /home
    api/dio_client.dart        dioProvider: 15 s timeouts, Bearer token, 401 → session expiry signal
    api/problem.dart           RFC 9457 Problem Details → { title, status, traceId, fieldErrors (camelCase) }
    api/session_expiry.dart    signal from the interceptor to the auth controller (avoids a provider cycle)
    ui/                        MessageView, ErrorRetryView (offline / 403 / error + Retry), InlineLoadError
  features/
    auth/                      models, token_storage (flutter_secure_storage), auth_repository, auth_controller,
                               login_screen, register_screen
    home/home_screen.dart      role-based Phase 0 placeholders
    notifications/             app-level status watcher, local notifications and the permission ask (UC08; only
                               while the app process is alive, see the root README)
test/                          helpers.dart (FakeTokenStorage, MockAuthRepository, pumpApp), core/, features/
```

## Auth rules

- The token, its expiry and the user are stored only in flutter_secure_storage (§15.2), never SharedPreferences.
- On startup: an expired session is cleared; otherwise `GET /api/auth/me` confirms it (401 → cleared; offline →
  the unexpired session is kept).
- Facilities Officers and Admins get no session: "This app is for students, lecturers and lab technicians. Please use
  the CampusSpace web portal."
- Any 401 (except `POST /api/auth/login`) signs an authenticated user out, and the router shows login.

## Android

- `minSdk = 24`: flutter_secure_storage 11.x requires it (also Flutter 3.47's default).
- `INTERNET` is in the main manifest, so release builds have network access.
- Cleartext HTTP is allowed only in debug builds and only to `10.0.2.2` and `localhost`
  (`android/app/src/debug/res/xml/network_security_config.xml`). Release builds are HTTPS-only.
- Release APK builds come in Phase 6.
