# Changelog

Toutes les versions notables de hdr2sdr.

Le format est base sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/).

## [1.0.0] - 2026-09-15

Premiere version publique.

### Ajoute
- Basculer HDR / SDR (API Windows 11 24H2 `SET_HDR_STATE` + fallbacks)
- Luminosite 0–100 % :
  - en **HDR** : curseur Windows « luminosite du contenu SDR »
  - en **SDR** : luminosite moniteur via DDC/CI
- Fenetre principale (plus de demarrage force en zone de notification)
- Option « Minimiser dans la zone de notification »
- Lancement au demarrage de Windows
- Profils Jour / Soir / Jeu
- Raccourcis clavier configurables (activer / desactiver / modifier)
- Maintien des raccourcis Lum+ / Lum− pour variation continue (DDC optimise)
- Menu Options (engrenage)
- Raccourci menu Demarrer (mis a jour a chaque build / lancement)
- CLI silencieuse : `status`, `toggle`, `on`, `off`, `sdr`, `profile`
- Helpers `.vbs` sans fenetre console

### Notes
- Pensé pour le Philips Evnia 27M2N8500 (QD-OLED / DisplayHDR True Black 400), compatible avec d'autres écrans HDR Win11
- Profil Soir : HDR ON + SDR 0 %
- Profil Jeu : HDR ON + SDR 80 % (ajustable via config)

[1.0.0]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.0.0
