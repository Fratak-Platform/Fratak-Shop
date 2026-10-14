#!/usr/bin/env bash
# Runs a command, keeps its full output in a log file, and turns every error/warning
# line into a GitHub annotation so failures are visible directly on the run.
set -uo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 <log-file> <command...>" >&2
  exit 2
fi

log="$1"
shift

"$@" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}

if [ "$status" -ne 0 ]; then
  echo "::group::Errors and warnings (from $log)"
  grep -iE "error|warning" "$log" | awk '!seen[$0]++' | head -300 | while IFS= read -r line; do
    msg=${line//'%'/'%25'}
    msg=${msg//$'\r'/}
    echo "::error::${msg:0:900}"
  done
  echo "::endgroup::"
fi

exit "$status"
