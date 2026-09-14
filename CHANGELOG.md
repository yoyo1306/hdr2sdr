# Changelog

Toutes les versions notables de hdr2sdr.

Le format est basé sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/).

## [1.0.0] - 2026-09-15

Première version publique.

### Ajouté
- Basculer HDR / SDR (API Windows 11 24H2 `SET_HDR_STATE` + fallbacks)
- Luminosité 0–100 % :
  - en **HDR** : curseur Windows « luminosité du contenu SDR »
  - en **SDR** : luminosité moniteur via DDC/CI
- Fenêtre principale (plus de démarrage forcé en zone de notification)
- Option « Minimiser dans la zone de notification »
- Lancement au démarrage de Windows
- Profils Jour / Soir / Jeu
- Raccourcis clavier configurables (activer / désactiver / modifier)
- Maintien des raccourcis Lum+ / Lum− pour variation continue (DDC optimisé)
- Menu Options (engrenage)
- Raccourci menu Démarrer (mis à jour à chaque build / lancement)
- CLI silencieuse : `status`, `toggle`, `on`, `off`, `sdr`, `profile`
- Helpers `.vbs` sans fenêtre console

### Notes
- Pensé pour le Philips Evnia 27M2N8500 (QD-OLED / DisplayHDR True Black 400), compatible avec d'autres écrans HDR Win11
- Profil Soir : HDR ON + SDR 0 %
- Profil Jeu : HDR ON + SDR 80 % (ajustable via config)

[1.0.0]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.0.0
