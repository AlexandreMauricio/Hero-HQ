#!/usr/bin/env bash
# Runs every EditMode test with no window and prints the totals.
# The Unity editor must be closed (one project can only be open once).
# Usage: tools/test.sh            (all EditMode tests)
#        tools/test.sh <filter>   (only tests whose name matches, e.g. ProjectSetupTests)
set -u
cd "$(dirname "$0")/.."
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe}"
mkdir -p TestResults
RESULTS="$(pwd -W 2>/dev/null || pwd)/TestResults/editmode.xml"
LOG="$(pwd -W 2>/dev/null || pwd)/TestResults/editmode.log"
rm -f TestResults/editmode.xml
ARGS=(-batchmode -projectPath "$(pwd -W 2>/dev/null || pwd)" -runTests -testPlatform EditMode
      -testResults "$RESULTS" -logFile "$LOG")
[ $# -gt 0 ] && ARGS+=(-testFilter "$1")
"$UNITY" "${ARGS[@]}"
CODE=$?
if [ ! -f TestResults/editmode.xml ]; then
  echo "No results file: Unity did not run the tests (exit $CODE). See TestResults/editmode.log"
  exit 1
fi
# The first <test-run> line holds the totals.
LINE=$(grep -m1 -o '<test-run[^>]*>' TestResults/editmode.xml)
get() { echo "$LINE" | grep -o " $1=\"[^\"]*\"" | cut -d'"' -f2; }
echo "$(get total) tests: $(get passed) passed, $(get failed) failed, $(get skipped) skipped ($(get result))"
grep -o '<test-case [^>]*result="Failed"[^>]*>' TestResults/editmode.xml | grep -o ' fullname="[^"]*"' | sed 's/ fullname=/  FAILED: /'
exit $CODE
