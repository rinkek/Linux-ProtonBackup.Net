#!/usr/bin/env bash
# Builds a .deb with the daemon, the UI and the systemd units. The Proton Drive CLI is
# deliberately not part of it: the app finds and downloads it itself.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
PACKAGE=protonbackup
ARCH=amd64
RUNTIME=linux-x64
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/Directory.Build.props")"
[ -n "$VERSION" ] || { echo "No version found in Directory.Build.props"; exit 1; }

STAGE="$HERE/build/$PACKAGE-$VERSION"
OUTPUT="$HERE/${PACKAGE}_${VERSION}_${ARCH}.deb"

echo "== $PACKAGE $VERSION ($ARCH) =="
rm -rf "$HERE/build" "$OUTPUT"
mkdir -p "$STAGE"/{DEBIAN,usr/bin,usr/lib/protonbackup/daemon,usr/lib/protonbackup/ui,usr/lib/systemd/user,usr/share/applications,usr/share/doc/$PACKAGE}
mkdir -p "$STAGE/usr/share/icons/hicolor/256x256/apps"

publish() {
    local project="$1" output="$2"
    echo "-- publishing: $project"
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

# The whole publish folder, not just the binary: a single-file publish leaves native
# libraries such as e_sqlite3.so and libSkiaSharp.so next to it.
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
Recommends: libice6, libsm6, libxext6, libxrandr2, libxi6, libxcursor1
Installed-Size: $INSTALLED_KB
Maintainer: Rinke Kleijer <rkl_shop@hotmail.com>
Description: One-way backup of local folders to Proton Drive
 Copies local folders to Proton Drive and uploads only new and changed files. Never
 deletes or replaces anything on Proton. Runs as a systemd user timer, with a desktop
 app for the settings, the status and signing in.
 .
 The official proton-drive CLI is not part of this package; the app finds it and offers
 to download it, with a check of its SHA-512.
EOF

# Maintainer scripts: each script plus the shared helpers pasted in at its "@COMMON@" line.
for script in postinst prerm postrm; do
    sed -e '/^# @COMMON@$/{' -e "r $HERE/debian/common.sh" -e 'd' -e '}' \
        "$HERE/debian/$script" > "$STAGE/DEBIAN/$script"
    chmod 755 "$STAGE/DEBIAN/$script"
    ! grep -q '@COMMON@' "$STAGE/DEBIAN/$script" || { echo "$script: the shared helpers were not pasted in"; exit 1; }
    sh -n "$STAGE/DEBIAN/$script"
done

# xz: Debian 11's dpkg cannot read the zstd that a current dpkg uses by default.
dpkg-deb -Zxz --root-owner-group --build "$STAGE" "$OUTPUT" >/dev/null
rm -rf "$HERE/build"

echo
echo "Ready: $OUTPUT"
dpkg-deb --info "$OUTPUT" | sed -n '2,12p'
