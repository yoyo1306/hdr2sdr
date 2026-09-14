# hdr2sdr

Utilitaire **Windows 11** pour basculer rapidement **HDR ↔ SDR** et regler la **luminosite** (0–100 %).

Pense pour les ecrans OLED gaming (ex. Philips Evnia 27M2N8500 / DisplayHDR True Black 400), utile des que le HDR Windows rend le bureau trop sombre ou trop penible a gerer.

## Fonctionnalites

- Basculer **HDR / SDR** (API Win11 24H2)
- Luminosite unifiee 0–100 % :
  - **HDR ON** → curseur Windows « contenu SDR »
  - **SDR** → luminosite hardware (DDC/CI)
- Profils **Jour / Soir / Jeu**
- Raccourcis clavier (modifiables, desactivables, maintien Lum+/−)
- Options : minimiser en zone de notification, demarrage Windows
- CLI + helpers `.vbs` silencieux
- Raccourci menu Demarrer

## Installation

1. Telecharge la derniere release (`hdr2sdr-vX.Y.Z.zip`)
2. Extrais et lance `hdr2sdr.exe`
3. (Optionnel) coche « Lancer au demarrage » dans Options (engrenage)

Ou build depuis les sources :

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\dist\hdr2sdr.exe
```

## Usage CLI

```text
hdr2sdr status
hdr2sdr toggle
hdr2sdr on
hdr2sdr off
hdr2sdr sdr 55
hdr2sdr sdr up
hdr2sdr sdr down
hdr2sdr profile jour
hdr2sdr profile soir
hdr2sdr profile jeu
```

## Raccourcis par defaut

| Raccourci | Action |
|-----------|--------|
| Ctrl+Alt+H | Basculer HDR / SDR |
| Ctrl+Alt+Up / Down | Luminosite + / − |
| Ctrl+Alt+1 / 2 / 3 | Profil Jour / Soir / Jeu |

Config : `%AppData%\hdr2sdr\config.ini`

## Profils

| Profil | Comportement |
|--------|----------------|
| Jour | HDR OFF (SDR natif) |
| Soir | HDR ON + SDR 0 % |
| Jeu | HDR ON + SDR 80 % |

## Changelog

Voir [CHANGELOG.md](CHANGELOG.md).

## Licence

Usage personnel / open source — pas de licence formelle pour l'instant.
