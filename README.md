# FidelSec Forensic Imager

Een forensisch disk imaging-tool voor Windows én Linux, gebouwd met .NET 8. De applicatie maakt bit-exacte schijfkopieën van fysieke schijven en berekent hashes voor integriteitsverificatie.

Er zijn twee UI-varianten:

| UI | Framework | Platform |
|---|---|---|
| `FidelSec.UI` | WPF | Windows only |
| `FidelSec.UI.Avalonia` | Avalonia | Windows + Linux |

---

## Benodigdheden

| Vereiste | Versie / Opmerking |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) | 8.0 of hoger |
| Windows 10/11 (x64) | Voor de WPF-versie of Avalonia op Windows |
| Ubuntu 22.04+ / Debian 12+ (x64) | Voor Avalonia op Linux |
| Beheerdersrechten | Vereist voor toegang tot ruwe schijven (`\\.\PhysicalDriveN` / `/dev/sdX`) |

---

## Stappen om te runnen

### 1. Repository klonen

```bash
git clone <repo-url>
cd FidelSec_forensics
```

### 2. NuGet-pakketten herstellen

```powershell
dotnet restore FidelSec.sln
```

---

## Avalonia UI — cross-platform (Windows & Linux)

### Windows

Bouw eerst de Windows-infrastructuur (WMI/Win32), daarna de Avalonia UI:

```powershell
# Stap 1: Infrastructure bouwen (vereist voor schijftoegang op Windows)
dotnet build src\FidelSec.Infrastructure\FidelSec.Infrastructure.csproj -c Debug

# Stap 2: Avalonia UI bouwen
dotnet build src\FidelSec.UI.Avalonia\FidelSec.UI.Avalonia.csproj -c Debug
```

Starten als beheerder (**absoluut pad vereist** — een relatief pad werkt niet bij UAC-elevatie):

```powershell
Start-Process "C:\Users\thijm\ID-Projecten\Forensics\FidelSec_forensics\src\FidelSec.UI.Avalonia\bin\Debug\net8.0\FidelSec.UI.Avalonia.exe" -Verb RunAs
```

> **Waarom absoluut pad?** Bij `-Verb RunAs` start Windows het proces in een andere werkdirectory (`C:\Windows\System32`), waardoor relatieve paden niet werken.

### Linux

```bash
# Stap 1: Infrastructure en Avalonia UI bouwen
dotnet build src/FidelSec.Infrastructure.Linux/FidelSec.Infrastructure.Linux.csproj -c Debug
dotnet build src/FidelSec.UI.Avalonia/FidelSec.UI.Avalonia.csproj -c Debug

# Stap 2: Starten met root-rechten (vereist voor /dev/sdX toegang)
sudo dotnet run --project src/FidelSec.UI.Avalonia/FidelSec.UI.Avalonia.csproj -c Debug
```

Of na bouwen de binary direct starten:

```bash
sudo src/FidelSec.UI.Avalonia/bin/Debug/net8.0/FidelSec.UI.Avalonia
```

---

## WPF UI — Windows only

De originele WPF-interface. Werkt uitsluitend op Windows.

```powershell
# Bouwen
dotnet build src\FidelSec.UI\FidelSec.UI.csproj -c Debug

# Starten als beheerder (absoluut pad)
Start-Process "C:\src\FidelSec.UI\bin\Debug\net8.0-windows\FidelSec.UI.exe" -Verb RunAs
```

Of via de build-script (bouwt en publiceert naar `build/`):

```powershell
.\build.ps1 -WPF          # Debug WPF build
.\build.ps1 -WPF -Release # Release WPF build
.\build.ps1 -Test         # Met unit tests
```

Start daarna de gepubliceerde `.exe` als beheerder:

```
build\Debug\WPF\FidelSec.UI.exe   (rechtermuisknop → Als administrator uitvoeren)
```

---

## Logbestanden

Logs worden automatisch weggeschreven naar:

```
C:\ProgramData\FidelSec\Logs\fidelSec-<datum>.log   (Windows)
/var/log/fidelsec/fidelSec-<datum>.log               (Linux)
```

---

## Projectstructuur

| Map | Inhoud | Platform |
|---|---|---|
| `src/FidelSec.Core` | Interfaces en domeinmodellen | Alle |
| `src/FidelSec.ImagingEngine` | Raw imaging-logica | Alle |
| `src/FidelSec.Infrastructure` | WMI-apparaatdetectie, Win32-schijftoegang, hashing, logging | Windows |
| `src/FidelSec.Infrastructure.Linux` | lsblk-apparaatdetectie, libc-schijftoegang | Linux |
| `src/FidelSec.UI.Avalonia` | Avalonia-gebruikersinterface (MVVM, cross-platform) | Windows + Linux |
| `src/FidelSec.UI` | WPF-gebruikersinterface (MVVM, legacy) | Windows |
| `tests/FidelSec.Tests` | Unit tests | Alle |
