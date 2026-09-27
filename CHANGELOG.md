# Changelog

Toutes les versions notables de hdr2sdr.

Le format est basé sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/).

## [Unreleased]

## [1.2] - 2026-09-27

### Ajouté
- Choix de l'écran dans Options : liste des moniteurs détectés, mémorisé
  en config (`MonitorDevice=`, vide = écran principal)
- Option « Démarrer minimisé dans la zone de notification » (nécessite le
  mode tray, persistée en config `StartMinimized=`)

### Modifié
- Version affichée en bas à droite (`v1.2`)
- Fenêtre allégée : textes de pied et bouton « Basculer HDR / SDR » retirés
  (le basculement reste sur l'interrupteur et le menu de la zone de notification)

### Corrigé
- Luminosité lue au lancement sur l'écran choisi (lecture DDC 64 bits : un
  second écran ne force plus 100 %)
- Pourcentage non récupéré au lancement (SDR) : l'UI affichait toujours
  50 % par défaut sans lire le moniteur, et un échec DDC pouvait écraser
  l'affichage avec 0 %. Resync DDC frais en fond avec retries
  (0 / 0,6 / 1,5 / 3 / 6 s), UI mise à jour uniquement sur lecture réussie
  (jamais de 0 % d'échec), sans bloquer ni écraser un réglage en cours
- Sortie HDR → SDR : la luminosité hardware mémorisée avant l'entrée en HDR
  est réappliquée, et le curseur suit cette valeur
- Démarrage minimisé visible dans Alt+Tab : masquage direct dans le tray
  au chargement (sans passer par `WindowState`, dont l'événement ne part
  pas toujours avant le premier affichage)
- Tooltip du tray figé après les drags : suit maintenant chaque changement
  (même format que le statut)
- UI figée par le DDC : sliders/boutons/raccourcis n'écrivent plus jamais sur
  le thread UI (thread dédié), `RefreshStatus` en lecture seule ; les toggles
  HDR et la fermeture gardent l'écriture synchrone ordonnée
- DDC silencieux : le handle n'est plus jamais gardé ouvert (conflits CLI /
  Stream Deck, handles périmés après veille/boot), écriture vérifiée par
  relecture avec retry, rafale de sortie HDR maintenue, reinforce via worker,
  invalidation sur changement d'écran
- Raccourcis perdus au boot : ré-affirmation automatique toutes les 20 s +
  sur changement d'écran (sans intervention), avertissement ⚠ dans le tooltip
  du tray tant qu'une touche manque
- Raccourcis muets au démarrage (fenêtre jamais ouverte) : ré-assertion
  systématique à 3/10/30/60 s après le lancement même sans échec détecté,
  sur retour du tray, recréation de handle et événements système
  (DisplaySettings/Session/Power) ; `HideToTray` ne bloque plus le thread UI
  avec du DDC ; `RegisterHotKey` avec `EnsureHandle` + code d'erreur loggé
  (`%TEMP%\hdr-hk-log.txt`)
- Instance GUI unique (mutex) : fini les doublons (trays fantômes, conflits
  de hotkeys et DDC)

## [1.1.1] - 2026-09-18

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

[1.2]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.2
[1.1.1]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.1.1
[1.1.0]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.1.0
[1.0.0]: https://github.com/yoyo1306/hdr2sdr/releases/tag/v1.0.0
