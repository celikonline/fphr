#!/usr/bin/env bash
# Ağır web komutları (build/test) için slot: pnpm-slot.sh exec vite build
exec "$(dirname "$0")/slot.sh" pnpm "$@"
