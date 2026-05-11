# FidelSec Forensic Imager

Een forensisch disk imaging-tool voor Windows, gebouwd met .NET 8 en WPF. De applicatie maakt bit-exacte schijfkopieën van fysieke schijven via directe Win32-toegang en berekent hashes voor integriteitsverificatie.

---

## Benodigdheden

| Vereiste | Versie / Opmerking |
|---|---|
| Windows | 10 of 11 (x64) |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) | 8.0 of hoger |
| Beheerdersrechten | Vereist voor toegang tot ruwe schijven |

> De applicatie gebruikt WMI (`Win32_DiskDrive`) en directe Win32-schijftoegang. Hiervoor zijn elevated privileges nodig.

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

### 3a. Starten via de build-script (aanbevolen)

De meegeleverde `build.ps1` bouwt en publiceert de applicatie naar de `build/`-map.

```powershell
# Debug build
.\build.ps1

# Release build (geoptimaliseerd)
.\build.ps1 -Release

# Portable build (inclusief .NET runtime, geen installatie nodig)
.\build.ps1 -Release -SelfContained

# Met unit tests
.\build.ps1 -Test
```

Start daarna de gepubliceerde `.exe` **als beheerder**:

```
build\Debug\FidelSec.UI.exe   (rechtermuisknop → Als administrator uitvoeren)
```

### 3b. Starten via `dotnet` (ontwikkeling)

`dotnet run` kan de app niet direct als beheerder opstarten vanwege het `requireAdministrator`-manifest. Bouw de applicatie eerst en start de `.exe` dan elevated:

```powershell
# Bouwen
dotnet build src\FidelSec.UI\FidelSec.UI.csproj -c Debug

# Starten als beheerder
Start-Process "src\FidelSec.UI\bin\Debug\net8.0-windows\FidelSec.UI.exe" -Verb RunAs
```

---

## Logbestanden

Logs worden automatisch weggeschreven naar:

```
C:\ProgramData\FidelSec\Logs\fidelSec-<datum>.log
```

---

## Projectstructuur

| Map | Inhoud |
|---|---|
| `src/FidelSec.Core` | Interfaces en domeinmodellen |
| `src/FidelSec.Infrastructure` | WMI-apparaatdetectie, schijftoegang, hashing, logging |
| `src/FidelSec.ImagingEngine` | Raw imaging-logica |
| `src/FidelSec.UI` | WPF-gebruikersinterface (MVVM) |
| `tests/FidelSec.Tests` | Unit tests |
