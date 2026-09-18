# Changelog

Toutes les versions notables de hdr2sdr.

Le format est basé sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/).

## [Unreleased]

### Corrigé
- Clic sur l'icône de la barre des tâches minimise/restaure la fenêtre
  (`MinimizeBox=true`, invisible en borderless)

## [1.1.0] - 2026-09-18

Interface modernisée, zéro dépendance supplémentaire.

### Ajouté
- Thème clair / sombre : bouton ☀/☾ dans l'en-tête, persisté en config (`Theme=`)
  et appliqué sans restart (remapping complet des deux fenêtres)

### Modifié
- Nouvelle interface (fenêtre borderless, coins arrondis, dark / OLED-friendly)
- Hero card avec badge HDR/SDR + grand % + toggle switch animé
- Slider custom + boutons − / + , cartes Profils Jour / Soir / Jeu avec sélection visuelle
- Options modernisées (même config `config.ini`, aucun breaking change)
- Zéro dépendance supplémentaire (toujours `csc.exe` + WinForms uniquement)

### Corrigé
- Slider : logique d'origine restaurée (écriture immédiate à chaque mouvement,
  comme le TrackBar d'origine, visuel moderne conservé) + peinture synchrone
  du thumb et du % avant l'écriture DDC (zéro latence d'affichage) + écritures
  DDC sur thread dédié (l'UI ne bloque plus jamais, drag parfaitement fluide)
- Dimensionnement : `AutoScaleMode.None`, fenêtre verrouillée à 400×656,
  footer et bouton ancrés en bas
- Coins : arrondis restaurés partout (fenêtre, badge, cartes, profils) avec
  peinture sûre (fond natif conservé) ; bordures claires vérifiées au pixel,
  zéro pixel sombre mesuré
- Déplacement fenêtre : référentiel écran unique + capture souris (fini le
  saut vers le haut quand on attrapait le titre), garde anti-blocage

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

[1.1.0]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.1.0
[1.0.0]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.0.0
