#!/bin/zsh
# Runs one batch Unity command on the project, one at a time across agents (a mkdir lock), and prints the log's errors.
# The Unity editor must be closed: two Unity instances cannot open the same project.
# Usage: tools/unity-batch.sh <log name> <unity args...>
#   tools/unity-batch.sh story -executeMethod SdfStory.Editor.StoryCapture.CaptureInBatch -comic beat-fungal
#   tools/unity-batch.sh web -buildTarget WebGL -executeMethod SdfMushrooms.Editor.WebBuild.Build
#   tools/unity-batch.sh tests -runTests -testPlatform EditMode -testResults /tmp/sdf-tests.xml   (no -quit with -runTests)
ROOT=${0:A:h:h}
LOCK=${TMPDIR:-/tmp}/sdf-unity.lock
LOGS=${TMPDIR:-/tmp}/sdf-unity-logs; mkdir -p $LOGS
UNITY=/Applications/Unity/Hub/Editor/6000.3.0f1/Unity.app/Contents/MacOS/Unity
name=$1; shift
if pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath $ROOT/Unity" >/dev/null; then echo "The Unity editor is open on the project: close it first"; exit 2; fi
until mkdir $LOCK 2>/dev/null; do echo "waiting for the Unity lock (held by $(cat $LOCK/owner 2>/dev/null))"; sleep 10; done
echo $name > $LOCK/owner
trap 'rm -rf $LOCK' EXIT
log=$LOGS/$name-$(date +%H%M%S).log
quit=(-quit); [[ " $* " == *" -runTests "* ]] && quit=()
$UNITY -batchmode $quit -projectPath $ROOT/Unity -logFile $log "$@"
code=$?
echo "exit $code, log: $log"
grep -E "error CS|Exception|failed" $log | grep -v "^\s*at " | head -40
exit $code
