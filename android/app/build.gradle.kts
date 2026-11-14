plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// The version comes from the Windows project (<Version> in MediaPlayerSauce.csproj), so one bump covers Windows and
// Android. versionCode must grow with every release or Android refuses the update: 1.2.3 -> 10203.
val appVersion: String = Regex("<Version>([^<]+)</Version>")
    .find(rootDir.resolve("../src/MediaPlayerSauce/MediaPlayerSauce.csproj").readText())!!.groupValues[1].trim()
val versionParts = appVersion.split('.', '-').take(3).map { it.toInt() }

android {
    namespace = "com.sauceedition.mediaplayer"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.sauceedition.mediaplayer"
        minSdk = 24
        targetSdk = 35
        versionCode = versionParts[0] * 10000 + versionParts[1] * 100 + versionParts[2]
        versionName = appVersion
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        // Phones and tablets (ARM). Leaving out the x86 copies of libVLC keeps the download about half the size.
        // (./gradlew -Pemulator ... adds the x86_64 copy, for trying it on a PC's Android emulator)
        ndk {
            abiFilters += listOf("arm64-v8a", "armeabi-v7a")
            if (project.hasProperty("emulator")) abiFilters += "x86_64"
        }
    }

    buildTypes {
        release {
            // libVLC is called from native code through JNI: shrinking would strip what it calls.
            isMinifyEnabled = false
        }
    }

    // One .apk for every phone (SauceStore offers a single Android download), with libVLC compressed inside it.
    packaging {
        jniLibs { useLegacyPackaging = true }
    }

    buildFeatures { buildConfig = true }

    // libVLC's licences (LGPL-2.1, GPL-2.0) go inside the .apk, as they go beside the Windows app
    sourceSets["main"].assets.srcDir(rootDir.resolve("../licenses"))
    androidResources {
        // Android's default list, plus .NET's licence (that's for the Windows app only)
        ignoreAssetsPattern = "!dotnet-LICENSE.txt:!.svn:!.git:!.ds_store:!*.scc:.*:<dir>_*:!CVS:!thumbs.db:!picasa.ini:!*~"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    lint {
        abortOnError = true
        checkReleaseBuilds = false
        // version nags, and style suggestions that don't change what the app does
        disable += setOf(
            "GradleDependency", "NewerVersionAvailable", "AndroidGradlePluginVersion", "OldTargetApi", "UseKtx",
            "Overdraw", "DataExtractionRules", "ChromeOsAbiSupport", "SelectedPhotoAccess"
        )
    }
}

kotlin {
    compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) }
}

dependencies {
    implementation("org.videolan.android:libvlc-all:3.6.5")
    implementation("androidx.appcompat:appcompat:1.7.1")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.recyclerview:recyclerview:1.4.0")
    implementation("androidx.media:media:1.7.0")
    implementation("androidx.documentfile:documentfile:1.0.1")
    testImplementation("junit:junit:4.13.2")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
}
