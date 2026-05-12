# FidelSec Forensic Imager — Modern GUI, No .NET

Deze versie heeft een veel mooiere frontend met dezelfde forensic workflow:

- Sidebar navigatie
- Dashboard
- Disk Imaging
- Case metadata
- Output instellingen
- MD5/SHA1/SHA256 hashing
- Hash verification
- Log viewer
- Dark forensic UI

Geen .NET, Avalonia, WPF of NuGet.

## Starten zonder installatie

```bash
sudo ./run-gui.sh
```

## Installeren op Ubuntu/Debian/Raspberry Pi OS

```bash
bash install/install-gui-ubuntu-debian.sh
sudo fidelsec-gui
```

## Dependencies

- python3
- python3-tk
- util-linux
- coreutils
