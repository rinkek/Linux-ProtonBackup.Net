#!/usr/bin/env bash
# Installs and removes the package in a clean container.
set -euo pipefail
cat <<'INNER' > /tmp/inner.sh
set -e
export DEBIAN_FRONTEND=noninteractive
echo "== 1. installeren =="
apt-get update -qq
apt-get install -y -qq /pkg/protonbackup_0.1.0_amd64.deb
echo
echo "== 2. geplaatste bestanden =="
dpkg -L protonbackup | grep -vE '^/usr(/lib|/share|/bin)?$' | sort
echo
echo "== 3. draait de daemon? =="
protonbackup --help | head -4
echo
echo "== 3b. sqlite werkt (database wordt aangemaakt)? =="
protonbackup list-sources && echo "database ok (lege lijst)"
echo
echo "== 3c. UI laadt haar native bibliotheken? =="
# Zonder scherm hoort hij op het display te struikelen, niet op een ontbrekende .so.
out="$(protonbackup-ui 2>&1 || true)"
if echo "$out" | grep -qi 'DllNotFoundException\|cannot open shared object'; then
  echo "FOUT: ontbrekende bibliotheek"; echo "$out" | head -5
else
  echo "geen ontbrekende bibliotheken (faalt zoals verwacht op het ontbrekende scherm)"
fi
echo
echo "== 4. units aanwezig =="
ls -1 /usr/lib/systemd/user/protonbackup*
echo
echo "== 5. desktop-bestand geldig? =="
if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate /usr/share/applications/protonbackup.desktop && echo "desktop-bestand ok"
else
  grep -c '^' /usr/share/applications/protonbackup.desktop >/dev/null && echo "desktop-bestand aanwezig"
fi
echo
echo "== 6. verwijderen =="
apt-get remove -y -qq protonbackup
echo
echo "== 7. is alles weg? =="
for f in /usr/bin/protonbackup /usr/lib/protonbackup /usr/lib/systemd/user/protonbackup-sync.timer /usr/share/applications/protonbackup.desktop; do
  if [ -e "$f" ]; then echo "ACHTERGEBLEVEN: $f"; else echo "weg: $f"; fi
done
echo
echo "== 8. pakketstatus =="
dpkg -l protonbackup 2>/dev/null | tail -1 || echo "niet meer geinstalleerd"
INNER
docker run --rm -v "$(pwd):/pkg:ro" -v /tmp/inner.sh:/inner.sh:ro "${IMAGE:-ubuntu:24.04}" bash /inner.sh
