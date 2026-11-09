#!/bin/bash
# Engine-free game rules and their EditMode tests in seconds on the laptop (no .NET SDK needed): compiles the given
# sources against tools/logic-tests/UnityShim and Unity's NUnit with the Unity editor's own .NET runtime and Roslyn,
# then runs every [Test] (not [Explicit]) by reflection.
#
#   tools/logic-tests/quick/run.sh [filter] -- <source files...>
#   e.g. T=unity/MoveItGames/Assets/Games/Tennis; tools/logic-tests/quick/run.sh Swing -- $T/Scripts/SwingDetector.cs \
#            $T/Scripts/TennisBall.cs $T/Scripts/TennisLevel.cs $T/Scripts/TennisMatch.cs $T/Scripts/TennisScore.cs \
#            $T/Scripts/TennisTournament.cs $T/Tests/Editor/*.cs
#
# The filter (optional) keeps tests whose name or class contains it. Unity's own test runner stays the reference.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
U=/d/Unity/6000.3.9f1/Editor/Data
NET=$(ls -d $U/NetCoreRuntime/shared/Microsoft.NETCore.App/* | head -1)
NUNIT=$(ls -d $ROOT/unity/MoveItGames/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll | head -1)
FILTER=""
if [ "$1" != "--" ]; then FILTER=$1; shift; fi
[ "$1" == "--" ] && shift
OUT=$(mktemp -d)
REFS=""
for f in $NET/System*.dll $NET/mscorlib.dll $NET/netstandard.dll; do
  case "$f" in *Native*) continue;; esac
  REFS="$REFS -r:$(cygpath -w $f)"
done
SRC=""
for f in "$@" $ROOT/tools/logic-tests/UnityShim/*.cs $HERE/Runner.cs; do SRC="$SRC $(cygpath -w $f)"; done
$U/NetCoreRuntime/dotnet.exe $(cygpath -w $U/DotNetSdkRoslyn/csc.dll) -nologo -nostdlib -noconfig -langversion:9 \
  -nowarn:CS1591,CS0618,CS0649,CS0169,CS0414 -target:exe -out:$(cygpath -w $OUT/tests.dll) $REFS -r:$(cygpath -w $NUNIT) $SRC
cp $NUNIT $OUT/
echo '{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "'$(basename $NET)'" } } }' > $OUT/tests.runtimeconfig.json
cd "$ROOT" && $U/NetCoreRuntime/dotnet.exe $(cygpath -w $OUT/tests.dll) $FILTER
