#!/usr/bin/env bash
# usage: slot.sh <komut...>  — SENSE_BUILD_SLOTS adet flock kilidinden birini alıp komutu koşar
set -euo pipefail
LOCK_DIR="${SENSE_LOCK_DIR:-/tmp/senseik-slots}"; mkdir -p "$LOCK_DIR"
mem_gb=$(awk '/MemAvailable/ {printf "%d", $2/1048576}' /proc/meminfo)
auto=$(( (mem_gb-8)/6 )); cpu=$(( $(nproc)/4 ))
[ "$auto" -gt "$cpu" ] && auto=$cpu
[ "$auto" -lt 2 ] && auto=2
SLOTS="${SENSE_BUILD_SLOTS:-$auto}"
while true; do
  for i in $(seq 1 "$SLOTS"); do
    exec {fd}>"$LOCK_DIR/slot.$i"
    if flock -n "$fd"; then "$@"; exit $?; fi
    exec {fd}>&-
  done
  sleep 2
done
