# ProtonBackup

One-way, automatic backup of local folders to [Proton Drive](https://proton.me/drive) on Linux.

Point it at one or more folders, sign in once, and it keeps them mirrored to Proton Drive in the background — new and changed files are uploaded, nothing already on Proton Drive is ever touched or deleted by a sync.

## Features

- **First-run wizard** — download the CLI, sign in, pick a folder, turn on the timer. Four steps and it's running.
- **Runs unattended** — a lightweight background daemon does the actual syncing on a systemd timer, so it keeps working even when the app itself is closed.
- **Multiple source folders**, each with its own destination path on Proton Drive.
- **Live status** — last run, next run, synced/pending/failed counts, sign-in state.
- **Run history and failure log**, with the reason behind each failed file.
- **Manual control** — sync now, force a full resync, or remove a source folder at any time.
- **Self-updating CLI** — checks for newer versions of Proton's own `proton-drive` CLI and can roll back if an update misbehaves.

## How it works

The app never uploads anything itself — it only shows status and drives the daemon via systemd. All actual scanning and uploading happens in the daemon, which shells out to Proton's official `proton-drive` CLI (downloaded on first use and checksum-verified, not bundled). A local SQLite database is the only thing shared between the two.

## Installing

Prebuilt as a `.deb` for Debian/Ubuntu-based distributions:

```sh
sudo apt install ./protonbackup_0.4.5_amd64.deb
```

Everything needed is bundled — no separate runtime to install first.

Only one implementation (.NET, Python or Java) is installed at a time: remove the other one completely, including its data, first.

## Getting started

1. Launch **Proton Drive backup** from your application menu.
2. Follow the welcome screen: it fetches and verifies the CLI, signs you in through your browser, and lets you pick a folder to back up.
3. Turn on the timer, or leave it off and use **Sync now** whenever you like.

## Uninstalling

First let the app clean up its own files in your home folder (a package may not delete them); your files on Proton Drive are never touched. Either use Settings → Removal in the window, or:

```sh
protonbackup --cleanup
```

Then:

```sh
sudo apt remove protonbackup
```

If the package was removed first, it prints the commands for removing what is left by hand (run them as yourself):

```sh
systemctl --user disable --now protonbackup-sync.timer
rm -rf ~/.local/share/ProtonBackup ~/.config/ProtonBackup ~/.cache/ProtonBackup
rm -f ~/.config/systemd/user/protonbackup-sync.service ~/.config/systemd/user/protonbackup-sync@.service ~/.config/systemd/user/protonbackup-sync.timer
rm -rf ~/.config/systemd/user/protonbackup-sync.timer.d
rm -f ~/.config/systemd/user/timers.target.wants/protonbackup-sync.timer ~/.local/share/systemd/timers/stamp-protonbackup-sync.timer
systemctl --user daemon-reload
```

---

*Not affiliated with or endorsed by Proton AG.*
