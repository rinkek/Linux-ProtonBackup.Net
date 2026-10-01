#!/usr/bin/env bash
# Fase 1, test 4: kan de CLI bij de opgeslagen sessie als hij vanuit een systemd user-service draait?
# Dit is het open punt uit het ontwerp dat bepaalt of de secret store volstaat of dat `pass` nodig is.
# Draai dit NA `proton-drive auth login`.

set -uo pipefail

CLI="${PROTON_DRIVE_CLI:-$HOME/.local/share/ProtonBackup/bin/proton-drive}"
REPORT="$(cd "$(dirname "$0")" && pwd)/probe-systemd-$(date +%Y%m%d-%H%M%S).txt"

log()     { printf '%s\n' "$*" | tee -a "$REPORT"; }
section() { log ""; log "=== $* ==="; }

[ -x "$CLI" ] || { echo "CLI niet gevonden: $CLI"; exit 1; }

log "Test 4: CLI vanuit een systemd user-service"
log "datum: $(date -Is)"
log "cli:   $CLI"

section "Omgeving van de desktopsessie"
log "XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-<leeg>}"
log "DBUS_SESSION_BUS_ADDRESS=${DBUS_SESSION_BUS_ADDRESS:-<leeg>}"
log "PROTON_DRIVE_CREDENTIALS_STORE=${PROTON_DRIVE_CREDENTIALS_STORE:-<niet gezet, standaard secret store>}"
log "linger: $(loginctl show-user "$USER" -p Linger --value 2>/dev/null || echo onbekend)"
log "secret-store daemons:"
pgrep -a -f 'gnome-keyring-daemon|kwalletd|keepassxc' 2>/dev/null | tee -a "$REPORT" || log "  (geen gevonden)"

section "A. Rechtstreeks in deze shell (baseline)"
timeout 60 "$CLI" filesystem list /my-files --json >/dev/null 2>&1
rc=$?
log "exit=$rc  $([ $rc -eq 0 ] && echo 'OK - sessie bereikbaar' || echo 'MISLUKT - log eerst in')"
[ $rc -ne 0 ] && { log ">> Eerst inloggen: $CLI auth login"; exit 1; }

section "B. Vanuit een transient systemd user-service"
log "\$ systemd-run --user --wait --pipe --collect $CLI filesystem list /my-files --json"
timeout 90 systemd-run --user --wait --pipe --collect \
    --unit="protonbackup-probe-$$" \
    "$CLI" filesystem list /my-files --json >/dev/null 2>>"$REPORT"
rc=$?
log "exit=$rc"
if [ $rc -eq 0 ]; then
    log ">> GOED: de service komt bij de sessie. De secret store volstaat; pass is niet nodig."
else
    log ">> PROBLEEM: de service komt NIET bij de sessie (exit $rc)."
    log ">> Terugvaloptie uit het ontwerp: PROTON_DRIVE_CREDENTIALS_STORE=pass"
fi

section "C. Omgeving zoals de service die ziet"
systemd-run --user --wait --pipe --collect --unit="protonbackup-env-$$" \
    /usr/bin/env 2>/dev/null | grep -E 'DBUS|XDG|PROTON|PATH=' | tee -a "$REPORT"

section "D. Met een expliciet lege D-Bus-omgeving (simuleert een kale service)"
timeout 90 systemd-run --user --wait --pipe --collect --unit="protonbackup-nodbus-$$" \
    --setenv=DBUS_SESSION_BUS_ADDRESS= \
    "$CLI" filesystem list /my-files --json >/dev/null 2>>"$REPORT"
log "exit=$?  (mislukken hier is te verwachten en verklaart waarom de sessie soms wegvalt)"

section "Nog handmatig te doen"
log "1. Vergrendel je scherm / sluit de keyring en draai dit script opnieuw -> werkt B dan nog?"
log "2. Herstart de machine, log in maar open GEEN terminal-wachtwoordprompt, draai dit opnieuw."
log "3. Zet zo nodig 'loginctl enable-linger $USER' aan en herhaal stap 2."
log ""
log "Rapport: $REPORT"
