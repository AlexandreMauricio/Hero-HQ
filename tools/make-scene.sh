#!/usr/bin/env bash
# Remakes Assets/Scenes/Battle.unity with BattleSceneBuilder, with no window (03 Architecture 7.4).
# The Unity editor must be closed (one project can only be open once). Never edit the scene by hand:
# change Assets/Scripts/Screens/Editor/BattleSceneBuilder.cs and run this again.
# Usage: tools/make-scene.sh
set -u
cd "$(dirname "$0")/.."
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe}"
SCENE=Assets/Scenes/Battle.unity
mkdir -p TestResults
LOG="$(pwd -W 2>/dev/null || pwd)/TestResults/make-scene.log"
STAMP=TestResults/make-scene.stamp
touch "$STAMP"
sleep 1
"$UNITY" -batchmode -quit -projectPath "$(pwd -W 2>/dev/null || pwd)" \
  -executeMethod HeroHQ.Screens.Editor.BattleSceneBuilder.Build -logFile "$LOG"
CODE=$?
if [ $CODE -ne 0 ]; then
  echo "Unity failed (exit $CODE). See TestResults/make-scene.log"
  grep -m5 -E "error CS|Exception|executeMethod" TestResults/make-scene.log
  exit $CODE
fi
# The scene must be new, not left over from an earlier run.
if [ ! "$SCENE" -nt "$STAMP" ]; then
  echo "Unity exited 0 but did not write $SCENE. See TestResults/make-scene.log"
  exit 1
fi
echo "Made $SCENE"
