# FidelSec Touch — Touchscreen GUI zonder .NET

Touchscreen-vriendelijke versie van FidelSec Forensic Imager.

## Gemaakt voor

- Raspberry Pi touchscreen
- Ubuntu touchscreen
- Debian Bookworm
- Raspberry Pi OS Bookworm
- 1024x600 en 1280x800 schermen

## Touch features

- Grote knoppen
- Grote tekst
- Disk selectie via touch-kaarten
- Minder kleine dropdowns
- Fullscreen met F11
- Escape sluit fullscreen
- Simpele workflow: Home → Imaging → Verify → Logs

## Geen .NET

Gebruikt alleen:

- Python 3
- Tkinter
- Linux tools: lsblk, blockdev, coreutils

## Starten zonder installatie

```bash
sudo ./run-touch.sh
```

## Installeren

```bash
bash install/install-touch-ubuntu-debian.sh
sudo fidelsec-touch
```

## Raspberry Pi tip

Voor touch-kiosk/fullscreen:

```bash
sudo fidelsec-touch
```

Druk op `F11` voor fullscreen.
