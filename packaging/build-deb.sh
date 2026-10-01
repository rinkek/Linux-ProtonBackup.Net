#!/usr/bin/env bash
# Bouwt een .deb met de daemon, de UI en de systemd-units. De Proton Drive CLI zit er
# bewust niet in: de app zoekt en downloadt die zelf.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
PACKAGE=protonbackup
ARCH=amd64
RUNTIME=linux-x64
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/Directory.Build.props")"
[ -n "$VERSION" ] || { echo "Geen versienummer gevonden in Directory.Build.props"; exit 1; }

STAGE="$HERE/build/$PACKAGE-$VERSION"
OUTPUT="$HERE/${PACKAGE}_${VERSION}_${ARCH}.deb"

echo "== $PACKAGE $VERSION ($ARCH) =="
rm -rf "$HERE/build" "$OUTPUT"
mkdir -p "$STAGE"/{DEBIAN,usr/bin,usr/lib/protonbackup/daemon,usr/lib/protonbackup/ui,usr/lib/systemd/user,usr/share/applications,usr/share/doc/$PACKAGE}
mkdir -p "$STAGE/usr/share/icons/hicolor/256x256/apps"

publish() {
    local project="$1" output="$2"
    echo "-- publiceren: $project"
    "$DOTNET" publish "$ROOT/$project" \
        --configuration Release \
        --runtime "$RUNTIME" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:DebugType=none \
        --output "$output" \
        --nologo --verbosity quiet
}

TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

publish ProtonBackup.Daemon "$TEMP/daemon"
publish ProtonBackup.UI "$TEMP/ui"

# De hele publicatiemap, niet alleen de binary: een single-file publish laat native
# bibliotheken zoals e_sqlite3.so en libSkiaSharp.so ernaast staan.
cp -a "$TEMP/daemon/." "$STAGE/usr/lib/protonbackup/daemon/"
cp -a "$TEMP/ui/."     "$STAGE/usr/lib/protonbackup/ui/"
find "$STAGE/usr/lib/protonbackup" -type f -exec chmod 644 {} +
chmod 755 "$STAGE/usr/lib/protonbackup/daemon/protonbackup" "$STAGE/usr/lib/protonbackup/ui/protonbackup-ui"
ln -sf ../lib/protonbackup/daemon/protonbackup "$STAGE/usr/bin/protonbackup"
ln -sf ../lib/protonbackup/ui/protonbackup-ui  "$STAGE/usr/bin/protonbackup-ui"

install -m 644 "$HERE/templates/protonbackup-sync.service"  "$STAGE/usr/lib/systemd/user/"
install -m 644 "$HERE/templates/protonbackup-sync@.service" "$STAGE/usr/lib/systemd/user/"
install -m 644 "$HERE/templates/protonbackup-sync.timer"    "$STAGE/usr/lib/systemd/user/"
install -m 644 "$HERE/templates/protonbackup.desktop"       "$STAGE/usr/share/applications/"
install -m 644 "$HERE/protonbackup.png"                     "$STAGE/usr/share/icons/hicolor/256x256/apps/protonbackup.png"
install -m 644 "$HERE/templates/copyright"                  "$STAGE/usr/share/doc/$PACKAGE/copyright"

INSTALLED_KB="$(du -sk "$STAGE" | cut -f1)"

cat > "$STAGE/DEBIAN/control" <<EOF
Package: $PACKAGE
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Depends: libsecret-1-0, libfontconfig1, libx11-6
Recommends: libnotify-bin, libice6, libsm6, libxext6, libxrandr2, libxi6, libxcursor1
Installed-Size: $INSTALLED_KB
Maintainer: Rinke Kleijer <rkl_shop@hotmail.com>
Description: Eenrichtings-backup naar Proton Drive
 Kopieert lokale mappen naar Proton Drive en uploadt alleen nieuwe en gewijzigde
 bestanden. Verwijdert nooit iets op Proton. Draait als systemd user-timer met een
 desktop-app voor instellingen, status en inloggen.
 .
 De officiele proton-drive CLI zit niet in dit pakket; de app zoekt hem en biedt
 aan hem te downloaden, met controle van de SHA-512.
EOF

cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = configure ]; then
    systemctl --global daemon-reload >/dev/null 2>&1 || true
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
    fi
fi
exit 0
EOF

cat > "$STAGE/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
# Gebruikersdata blijft bewust staan; die ruim je op met: protonbackup --cleanup
if [ "$1" = remove ]; then
    echo "Let op: instellingen en de database in je thuismap blijven staan."
    echo "Ruim ze zo nodig eerst op met: protonbackup --cleanup"
fi
exit 0
EOF

chmod 755 "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/prerm"

fakeroot dpkg-deb --build --root-owner-group "$STAGE" "$OUTPUT" >/dev/null
rm -rf "$HERE/build"

echo
echo "Klaar: $OUTPUT"
dpkg-deb --info "$OUTPUT" | sed -n '2,12p'
