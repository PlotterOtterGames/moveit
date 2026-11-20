#!/bin/bash
# The box's C++ (pose_trust.h) against the lab (tracklab_trust.py), on the PC. Run in WSL from the repository's root,
# with the RKNN venv active (see convert.py):   bash tools/npu/trust_check.sh [<clip.mjpg>]
# It runs the lab on two stretches of the routine clip (hands and punches with the lite model; the walk up close, where
# the full model flips left and right) and on a few frames for the detector's input, then replays their model answers
# through the C++ and compares every number.
set -e
CLIP=${1:-motion-service-py/sessions/clips/routine-20261101.mjpg}
DARK=${2:-motion-service-py/sessions/clips/dark-20261110.mjpg}
OUT=${TRUST_CHECK_OUT:-/tmp/trust_check}
mkdir -p $OUT
g++ -std=c++17 -O2 -Wall -I unity/MoveItGames/Assets/Plugins/Android/MoveItMotion.androidlib/src/main/cpp \
    tools/npu/trust_check.cpp -o $OUT/trust_check
quiet() { "$@" 2>&1 | grep -v -E '^INFO: |XNNPACK|^W0000|^WARNING: All log|^I0000|absl::InitializeLog|RuntimeWarning|return 1.0 /' || true; }
quiet python tools/npu/tracklab.py run $CLIP --model lite --out $OUT --variant trust --first 440 --last 640 --dump $OUT/lite.bin
quiet python tools/npu/tracklab.py run $CLIP --model full --out $OUT --variant trust --first 1060 --last 1180 --dump $OUT/full.bin
$OUT/trust_check landmarks $OUT/lite.bin
$OUT/trust_check landmarks $OUT/full.bin
if [ -f $DARK ]; then
  quiet python tools/npu/detector_dump.py $DARK 300 12 $OUT/eye.bin
  quiet python tools/npu/detector_dump.py $DARK 600 16 $OUT/eye_idle.bin --idle
  $OUT/trust_check detector $OUT/eye.bin
  $OUT/trust_check detector $OUT/eye_idle.bin
fi
rm -f $OUT/*.bin
