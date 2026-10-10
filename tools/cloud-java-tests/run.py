#!/usr/bin/env python3
"""Compiles the box's cloud client (Assets/Plugins/Android/MoveItCloud.androidlib) against the real Android
framework classes and runs its JUnit tests on this PC, with no Android SDK or Gradle.

- android-all (Robolectric's build of the Android framework, API 36) from Maven Central gives javac every
  android.* class, so the Android glue (installer, jobs, device owner) is compile-checked too, and org.json is real.
- Only the pure classes (GiftCode, SignedDoc, PassClock, ChannelManifest, CloudState, Downloader, ...) are
  *run*: Android behaviour itself (PackageInstaller, JobScheduler) still needs a box or MuMu.
The tests check the Java against the same vectors as the server: server/test/vectors/*.json.

Usage: python3 tools/cloud-java-tests/run.py   (needs a JDK 17+ on PATH; downloads ~200 MB once into cache/)
"""
import os
import shutil
import subprocess
import sys
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
LIB = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Plugins", "Android", "MoveItCloud.androidlib")
CACHE = os.path.join(HERE, "cache")
BUILD = os.path.join(HERE, "build")
MAVEN = "https://repo1.maven.org/maven2/"
JARS = {
    "android-all-16.jar": "org/robolectric/android-all/16-robolectric-13921718/android-all-16-robolectric-13921718.jar",
    "junit-4.13.2.jar": "junit/junit/4.13.2/junit-4.13.2.jar",
    "hamcrest-core-1.3.jar": "org/hamcrest/hamcrest-core/1.3/hamcrest-core-1.3.jar",
}
MIN_SIZE = {"android-all-16.jar": 100_000_000, "junit-4.13.2.jar": 300_000, "hamcrest-core-1.3.jar": 40_000}


def fetch(name, path):
    target = os.path.join(CACHE, name)
    if os.path.exists(target) and os.path.getsize(target) >= MIN_SIZE[name]:
        return target
    os.makedirs(CACHE, exist_ok=True)
    for attempt in range(8):  # Maven Central rate-limits bursts (HTTP 429)
        try:
            print(f"downloading {name} ...", flush=True)
            with urllib.request.urlopen(MAVEN + path, timeout=300) as r, open(target + ".tmp", "wb") as out:
                shutil.copyfileobj(r, out)
            if os.path.getsize(target + ".tmp") >= MIN_SIZE[name]:
                os.replace(target + ".tmp", target)
                return target
        except Exception as e:  # noqa: BLE001
            print(f"  attempt {attempt + 1}: {e}")
        time.sleep(15)
    sys.exit(f"could not download {name}")


def sources(root):
    out = []
    for d, _, files in os.walk(root):
        out += [os.path.join(d, f) for f in files if f.endswith(".java")]
    return sorted(out)


def main():
    jars = [fetch(n, p) for n, p in JARS.items()]
    android, junit, hamcrest = jars
    shutil.rmtree(BUILD, ignore_errors=True)
    main_out, test_out = os.path.join(BUILD, "main"), os.path.join(BUILD, "test")
    os.makedirs(main_out)
    os.makedirs(test_out)
    javac = ["javac", "-encoding", "UTF-8", "--release", "17", "-Xlint:all", "-Xlint:-options", "-Xlint:-classfile", "-Werror"]
    r = subprocess.run(javac + ["-cp", android, "-d", main_out] + sources(os.path.join(LIB, "src", "main", "java")))
    if r.returncode:
        sys.exit("main sources do not compile")
    print("compile check: MoveItCloud.androidlib OK against Android API 36")
    cp = os.pathsep.join([main_out, android, junit, hamcrest])
    tests = sources(os.path.join(HERE, "src"))
    r = subprocess.run(javac + ["-cp", cp, "-d", test_out] + tests)
    if r.returncode:
        sys.exit("tests do not compile")
    classes = [os.path.relpath(t, os.path.join(HERE, "src"))[:-5].replace(os.sep, ".") for t in tests if t.endswith("Test.java")]
    env = dict(os.environ, TUNDU_VECTORS=os.path.join(REPO, "server", "test", "vectors"))
    r = subprocess.run(["java", "-cp", os.pathsep.join([test_out, cp]), "org.junit.runner.JUnitCore"] + classes, env=env)
    sys.exit(r.returncode)


if __name__ == "__main__":
    main()
