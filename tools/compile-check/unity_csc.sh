#!/bin/bash
# Compile-check Unity game code on the laptop without opening the editor (no .NET SDK needed): compiles the asmdef
# folders you name, in order, with the Unity editor's own Roslyn against its UnityEngine module assemblies, the
# packages' compiled assemblies in Library/ScriptAssemblies (Input System, URP...) and the assemblies built before them
# in this run. Assemblies you don't name come from Library/ScriptAssemblies as Unity last built them.
#
#   tools/compile-check/unity_csc.sh [-d DEFINES] <folder>[=<assembly name>] ...
#   e.g. tools/compile-check/unity_csc.sh unity/MoveItGames/Assets/MoveSDK/Runtime=MoveIt.Motion \
#            unity/MoveItGames/Assets/Games/Dress/Scripts=MoveIt.Games.Dress
#
# Defines default to the Android player's (UNITY_ANDROID, no UNITY_EDITOR); -d "UNITY_EDITOR;UNITY_STANDALONE_WIN"
# checks the editor's view. Only errors are shown. Not a replacement for Unity's own compile: no source generators,
# no asmdef define constraints.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
U=/d/Unity/6000.3.9f1/Editor/Data
LIB=$ROOT/unity/MoveItGames/Library/ScriptAssemblies
DEFINES="UNITY_ANDROID;UNITY_6000_0_OR_NEWER;UNITY_2021_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2023_1_OR_NEWER;ENABLE_INPUT_SYSTEM"
if [ "$1" == "-d" ]; then DEFINES="$DEFINES;$2"; shift 2; fi
OUT=$(mktemp -d)
REFS="-r:$(cygpath -w $U/NetStandard/ref/2.1.0/netstandard.dll)"
for f in $U/Managed/UnityEngine/UnityEngine*.dll; do REFS="$REFS -r:$(cygpath -w $f)"; done
BUILT=""
for spec in "$@"; do
  DIR=${spec%%=*}
  NAME=${spec#*=}
  [ "$NAME" == "$spec" ] && NAME=$(basename "$DIR")
  SRC=$(find "$ROOT/$DIR" -name "*.cs" -not -path "*/Tests/*" -not -path "*/Editor/*" | while read f; do echo -n "$(cygpath -w "$f") "; done)
  LREFS=""
  for f in $LIB/*.dll; do
    b=$(basename $f .dll)
    case "$BUILT $NAME " in *" $b "*) continue;; esac
    case "$b" in *Editor*|*Tests*|*PlayTests*|*CodeGen*|MCPForUnity*) continue;; esac
    LREFS="$LREFS -r:$(cygpath -w $f)"
  done
  for b in $BUILT; do LREFS="$LREFS -r:$(cygpath -w $OUT/$b.dll)"; done
  echo "== $NAME ($(echo $SRC | wc -w) files)"
  $U/NetCoreRuntime/dotnet.exe $(cygpath -w $U/DotNetSdkRoslyn/csc.dll) -nologo -nostdlib -noconfig -langversion:9 -target:library \
    -nowarn:CS1591,CS0618,CS0649,CS0169,CS0414,CS0067 -define:"$DEFINES" -out:$(cygpath -w $OUT/$NAME.dll) $REFS $LREFS $SRC \
    | grep -E "error|rror CS" || echo "   ok"
  BUILT="$BUILT $NAME"
done
