# Changelog

## v2.0.0 — Modernisation Windows 11 (WinUI 3 / .NET 10)

### Nouveautés & modernisation
- **Réécriture complète en C# / .NET 10 (`x64`, `self-contained`)** avec séparation `Hdr2Sdr.Core` / `Hdr2Sdr.App` et zéro prérequis sur un PC Windows 11 vierge.
- **Interface native Windows 11 (WinUI 3)** : vrai fond **Mica**, barre de titre personnalisée, accent système, bascule **Clair / Sombre** mémorisée, cartes Fluent arrondies et affichage en temps réel des nits en mode HDR.
- **Panneau Options intégré (style Paramètres Windows 11)** : sélection de l’écran cible, réglage direct des pourcentages des profils **Soir** / **Jeu** et du pas `− / +`, démarrage Windows, zone de notification et capture interactive des 6 raccourcis clavier globaux.
- **Tray Win32 natif & instance unique améliorée** : plus aucune dépendance WinForms ; un second lancement ramène et focalise automatiquement la fenêtre existante (même réduite dans le tray), et les commandes CLI synchronisent instantanément l’interface ouverte.
- **Compatibilité `Program Files` (`AppPaths`)** : stockage sur place en mode portable ou dans `%LOCALAPPDATA%\hdr2sdr\` si installé dans `Program Files`, avec migration automatique d’une ancienne configuration `%APPDATA%\hdr2sdr\config.ini`.
- **Scripts `helpers/*.vbs` dynamiques** : détectent automatiquement `hdr2sdr.exe` en mode portable, dans `Program Files` ou via le `PATH`.
- **Installateur Inno Setup 6 (`hdr2sdr-Setup-2.0.0.exe`)** : compression `lzma2/ultra64`, style `modern dynamic`, langue française, arrêt automatique avant mise à jour, préservation de la configuration et ajout optionnel au `PATH` / démarrage Windows.
- **Archivage** : l’ancienne version v1.4 et ses helpers sont conservés dans `legacy/`.

## v1.4 (2026-10-17) — Version legacy (WinForms)
- Contrôle HDR / SDR DisplayConfig + luminosité SDR (`DwmpSDRToHDRBoost`) et DDC/CI (`dxva2.dll`).
- 3 profils (Jour, Soir, Jeu), raccourcis clavier globaux, zone de notification et mode CLI.
