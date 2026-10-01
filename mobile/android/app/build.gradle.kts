import java.util.Base64

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "lk.campusspace.campusspace_mobile"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        // flutter_local_notifications (10+) uses java.time APIs; desugaring backports them below API 26.
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "lk.campusspace.campusspace_mobile"
        // 24 (Android 7.0): flutter_secure_storage 11.x declares minSdk 24 (its Keystore-backed
        // ciphers), which is also Flutter 3.47's default. Pinned explicitly so a Flutter upgrade
        // cannot silently change it. Raise it only if a plugin requires more.
        minSdk = 24
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    buildTypes {
        release {
            // Signed with the debug key on purpose (Task 6.D4): the APK is a course deliverable installed fresh from
            // the GitHub Release, not a Play Store app, and a release keystore would need its passwords at every build.
            // A build from another machine has another signature: uninstall the old app before installing it.
            signingConfig = signingConfigs.getByName("debug")
        }
    }
}

// A release APK must call the deployed API over HTTPS: refuse to build it without
// --dart-define=API_URL=https://... (no silent fallback to the emulator's http://10.0.2.2:5080).
// Flutter passes the dart-defines as one comma-separated property of base64-encoded "NAME=value" entries.
val releaseApiUrl: String? =
    (findProperty("dart-defines") as String?)
        ?.split(",")
        ?.filter { it.isNotBlank() }
        ?.map { String(Base64.getDecoder().decode(it)) }
        ?.firstOrNull { it.startsWith("API_URL=") }
        ?.removePrefix("API_URL=")
        ?.trim()
tasks.matching { it.name == "compileFlutterBuildRelease" }.configureEach {
    doFirst {
        if (releaseApiUrl == null || !Regex("^https://[^/\\s]+").containsMatchIn(releaseApiUrl)) {
            throw GradleException(
                "A release build needs an https API URL. Build it with: " +
                    "flutter build apk --release --dart-define=API_URL=https://campusspace-api.onrender.com",
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}

dependencies {
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.5")
}
