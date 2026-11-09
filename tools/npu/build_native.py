"""Builds the NPU pose engine for the box (Android arm64) with Unity's NDK: libtundupose.so into the camera plugin's
jniLibs (the APK picks it up from there, next to Rockchip's librknnrt.so; it also holds the camera's V4L2 controls for
CameraTuner), and pose_npu_test, a shell tool for trying
the engine on the box without the app, into tools/npu/out/. Run after changing anything in
MoveItMotion.androidlib/src/main/cpp (the built library is committed, so APK builds don't need this step):
    motion-service-py/.venv/Scripts/python tools/npu/build_native.py
"""
import os
import subprocess
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
NDK = os.environ.get('ANDROID_NDK', r'D:\Unity\6000.3.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK')
MAIN = os.path.join(ROOT, 'unity', 'MoveItGames', 'Assets', 'Plugins', 'Android', 'MoveItMotion.androidlib', 'src', 'main')
CPP = os.path.join(MAIN, 'cpp')
JNI = os.path.join(MAIN, 'jniLibs', 'arm64-v8a')
OUT = os.path.join(ROOT, 'tools', 'npu', 'out')


def tool(name):
    host = 'windows-x86_64' if os.name == 'nt' else 'linux-x86_64'
    return os.path.join(NDK, 'toolchains', 'llvm', 'prebuilt', host, 'bin', name + ('.exe' if os.name == 'nt' else ''))


def build(sources, output, extra, api=28):
    cmd = [tool('clang++'), f'--target=aarch64-linux-android{api}', '-std=c++17', '-O2', '-fPIC', '-Wall',
           '-I', CPP, '-I', os.path.join(CPP, 'include')] + [os.path.join(CPP, s) for s in sources] + extra + [
           '-L', JNI, '-lrknnrt', '-llog', '-static-libstdc++', '-Wl,-z,max-page-size=16384', '-o', output]
    print(os.path.relpath(output, ROOT))
    subprocess.run(cmd, check=True)
    subprocess.run([tool('llvm-strip'), '--strip-unneeded', output], check=True)


def main():
    os.makedirs(OUT, exist_ok=True)
    build(['pose_npu.cpp', 'pose_npu_jni.cpp', 'camera_controls.cpp', 'frame_share.cpp'], os.path.join(JNI, 'libtundupose.so'),
          ['-shared', '-Wl,-soname,libtundupose.so', '-Wl,--no-undefined', '-ljnigraphics'])
    # the test tool decodes camera clips with Android 11's AImageDecoder (the box runs Android 11; the app stays at 28)
    build(['pose_npu.cpp', 'pose_npu_test.cpp'], os.path.join(OUT, 'pose_npu_test'), ['-ljnigraphics', '-pthread'],
          api=30)


if __name__ == '__main__':
    sys.exit(main())
