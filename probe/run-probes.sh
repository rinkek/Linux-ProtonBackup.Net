#!/usr/bin/env bash
# Fase 1 proefopstelling: beantwoordt de tests uit het ontwerp die een Proton-login nodig hebben.
# Draai dit NA `proton-drive auth login`. Alles gebeurt in een eigen testmap op Proton.

set -uo pipefail

CLI="${PROTON_DRIVE_CLI:-$HOME/.local/share/ProtonBackup/bin/proton-drive}"
REMOTE_BASE="${PROBE_REMOTE_BASE:-/my-files/ProtonBackupProbe}"
REMOTE_PARENT="$(dirname "$REMOTE_BASE")"
REMOTE_NAME="$(basename "$REMOTE_BASE")"
WORK="$(mktemp -d /tmp/protonprobe.XXXXXX)"
REPORT="$(cd "$(dirname "$0")" && pwd)/probe-report-$(date +%Y%m%d-%H%M%S).txt"

log()     { printf '%s\n' "$*" | tee -a "$REPORT"; }
section() { log ""; log "=============================================================="; log "$*"; log "=============================================================="; }
run()     { log "\$ $*"; timeout 300 "$@" 2>&1 | tee -a "$REPORT"; local rc=${PIPESTATUS[0]}; log "(exit $rc)"; return $rc; }

cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

[ -x "$CLI" ] || { echo "CLI niet gevonden of niet uitvoerbaar: $CLI"; exit 1; }

log "Proefopstelling Proton Drive backup-tool"
log "datum:       $(date -Is)"
log "cli:         $CLI"
log "cli-versie:  $("$CLI" version 2>&1 | head -1)"
log "testmap:     $REMOTE_BASE"
log "werkmap:     $WORK"

section "Sessiecontrole (voorwaarde voor de rest)"
if ! run "$CLI" filesystem list /my-files --json >/dev/null; then
    log ">> NIET INGELOGD. Draai eerst: $CLI auth login"
    exit 1
fi
log ">> Sessie werkt."

run "$CLI" filesystem create-folder "$REMOTE_PARENT" "$REMOTE_NAME"

# ---------------------------------------------------------------- test 1
section "Test 1: hetzelfde bestand twee keer uploaden naar hetzelfde pad"
mkdir -p "$WORK/t1"
printf 'versie 1\n' > "$WORK/t1/same.txt"

log "--- 1a: eerste upload (-f create-new-revision) ---"
run "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t1/same.txt" "$REMOTE_BASE"
run "$CLI" filesystem list "$REMOTE_BASE" --json

log "--- 1b: identieke inhoud nogmaals (verwacht: overgeslagen) ---"
run "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t1/same.txt" "$REMOTE_BASE"
run "$CLI" filesystem list "$REMOTE_BASE" --json

log "--- 1c: gewijzigde inhoud, zelfde naam (verwacht: nieuwe revisie, GEEN duplicaat) ---"
printf 'versie 2, gewijzigd\n' > "$WORK/t1/same.txt"
run "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t1/same.txt" "$REMOTE_BASE"
run "$CLI" filesystem list "$REMOTE_BASE" --json
run "$CLI" filesystem info "$REMOTE_BASE/same.txt" --json

log "--- 1d: ZONDER -f, niet-interactief (verwacht: prompt => daemon mag dit nooit doen) ---"
printf 'versie 3\n' > "$WORK/t1/same.txt"
log "\$ $CLI filesystem upload $WORK/t1/same.txt $REMOTE_BASE  < /dev/null"
timeout 45 "$CLI" filesystem upload "$WORK/t1/same.txt" "$REMOTE_BASE" </dev/null 2>&1 | tee -a "$REPORT"
rc=${PIPESTATUS[0]}
log "(exit $rc)"
[ "$rc" -eq 124 ] && log ">> TIMEOUT: de CLI bleef op invoer wachten. Strategie-vlag is verplicht in de daemon."

# ---------------------------------------------------------------- test 2
section "Test 2: uploaden naar een pad waarvan de tussenmappen nog niet bestaan"
mkdir -p "$WORK/t2"
printf 'diep\n' > "$WORK/t2/deep.txt"
DEEP="$REMOTE_BASE/niveau1/niveau2"
log "--- 2a: los bestand naar niet-bestaande parent ($DEEP) ---"
run "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t2/deep.txt" "$DEEP"
run "$CLI" filesystem list "$REMOTE_BASE" --json

log "--- 2b: hele lokale mapstructuur uploaden (verwacht: CLI maakt de boom zelf) ---"
mkdir -p "$WORK/t2tree/sub/subsub"
printf 'a\n' > "$WORK/t2tree/a.txt"
printf 'b\n' > "$WORK/t2tree/sub/b.txt"
printf 'c\n' > "$WORK/t2tree/sub/subsub/c.txt"
run "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t2tree" "$REMOTE_BASE"
run "$CLI" filesystem list "$REMOTE_BASE/t2tree" --json
run "$CLI" filesystem list "$REMOTE_BASE/t2tree/sub" --json

# ---------------------------------------------------------------- test 3
section "Test 3: tijdmeting - per bestand, gebundeld in een aanroep, en per map"
mkdir -p "$WORK/t3/each" "$WORK/t3/batch" "$WORK/t3/folder"
for i in $(seq 1 100); do
    head -c 4096 /dev/urandom > "$WORK/t3/each/f$i.bin"
    head -c 4096 /dev/urandom > "$WORK/t3/batch/f$i.bin"
    head -c 4096 /dev/urandom > "$WORK/t3/folder/f$i.bin"
done

log "--- 3a: eenmalige overhead van een enkele aanroep ---"
t0=$(date +%s.%N)
"$CLI" version >/dev/null 2>&1
t1=$(date +%s.%N)
log "opstarttijd CLI: $(echo "$t1 - $t0" | bc) s"

log "--- 3b: 100 bestanden, elk een eigen aanroep ---"
run "$CLI" filesystem create-folder "$REMOTE_BASE" "each"
t0=$(date +%s.%N)
for i in $(seq 1 100); do
    "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t3/each/f$i.bin" "$REMOTE_BASE/each" >/dev/null 2>&1
done
t1=$(date +%s.%N)
log "100 losse aanroepen: $(echo "$t1 - $t0" | bc) s"

log "--- 3c: 100 bestanden als argumenten in EEN aanroep ---"
run "$CLI" filesystem create-folder "$REMOTE_BASE" "batch"
t0=$(date +%s.%N)
"$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK"/t3/batch/*.bin "$REMOTE_BASE/batch" >/dev/null 2>&1
t1=$(date +%s.%N)
log "1 aanroep met 100 argumenten: $(echo "$t1 - $t0" | bc) s"

log "--- 3d: de map in een keer uploaden ---"
t0=$(date +%s.%N)
"$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/t3/folder" "$REMOTE_BASE" >/dev/null 2>&1
t1=$(date +%s.%N)
log "1 mapupload van 100 bestanden: $(echo "$t1 - $t0" | bc) s"

# ---------------------------------------------------------------- test 6
section "Test 6: vorm van filesystem list --json"
run "$CLI" filesystem list /my-files --json
run "$CLI" filesystem list "$REMOTE_BASE" -t file --json
run "$CLI" filesystem info "$REMOTE_BASE" --json

# ---------------------------------------------------------------- test 5 (aanvullend)
section "Test 5: hernoemen op afstand"
printf 'hernoem mij\n' > "$WORK/rename-me.txt"
run "$CLI" filesystem upload -f create-new-revision -d merge -t "$WORK/rename-me.txt" "$REMOTE_BASE"
run "$CLI" filesystem rename "$REMOTE_BASE/rename-me.txt" "hernoemd.txt"
run "$CLI" filesystem list "$REMOTE_BASE" -t file --json

section "Klaar"
log "Rapport: $REPORT"
log ""
log "Test 4 (systemd + secret store) draai je apart met: ./probe/probe-systemd.sh"
log "Opruimen op Proton (verplaatst de testmap naar de prullenbak):"
log "  $CLI filesystem trash $REMOTE_BASE"
