# PVP Chronicle Recorder · Guide OBS

Ce guide couvre la connexion, la bonne fenêtre, les bordures noires, la taille des fichiers et l’audio. Recorder ne fait que commander OBS et associer les vidéos locales.

## 1. Connexion

Dans OBS, ouvrir **Outils → Paramètres du serveur WebSocket**, activer le serveur, garder le port `4455` et définir un mot de passe local. Ne jamais exposer ce port. Dans `/pvprec`, saisir `ws://127.0.0.1:4455`, le même mot de passe, enregistrer puis tester.

## 2. Scène et capture

Créer une scène dédiée. Ajouter **Capture de jeu**, choisir la fenêtre “FINAL FANTASY XIV” de `ffxiv_dx11.exe`. Si OBS choisit mal, recréer la source ou utiliser Capture d’écran, puis recadrer la zone du jeu.

## 3. Bordures noires

Pour une image `1920×1080` avec `120 px` en bas :

```text
1080 - 120 = 960 px
```

Dans **Transformer → Modifier la transformation**, recadrer le bas de `120`, puis régler le canevas et la sortie sur `1920×960` et adapter à l’écran. Ne pas étirer. Vérifier un fichier test de 5 à 10 secondes.

## 4. Audio du jeu uniquement

Désactiver les audios du bureau et microphones inutiles. Ajouter **Capture audio d’application (BÊTA)** pour `ffxiv_dx11.exe`. Le navigateur, Discord, les notifications et le micro ne doivent pas bouger. Après un plantage, sélectionner à nouveau la nouvelle fenêtre si nécessaire.

## 5. Format et taille

Utiliser MKV, NVENC H.264 si disponible, qualité élevée/taille moyenne, 128 ou 160 Kbps et 30 FPS. À 6000+160 Kbps, compter environ 46 Mo/min. Pour réduire : 3500–5000 Kbps, CQP à partir de `CQ 24`, 30 FPS et aucune bordure. Tester une minute de combat après chaque changement.

## 6. Recorder

Dans `/pvprec`, choisir la langue, activer l’automatisation, saisir l’adresse et le mot de passe, laisser normalement le dossier FFmpeg vide, conserver `0 ms`, enregistrer, tester et installer Flyleaf. L’étalonnage ne sert qu’à un décalage fixe reproductible.

Les quatre champs ont un titre et une explication. La qualité ne crée aucune vidéo convertie : seule l’image en mémoire est redimensionnée, donc aucun cache proxy ne reste sur C:. L’original OBS et FFmpeg restent conservés.

## 7. Plantage, sécurité et vie privée

Après un plantage, vérifier OBS et arrêter manuellement l’enregistrement de l’écran de connexion. Les morceaux ne sont pas fusionnés. Ne jamais publier le port ou le mot de passe ; vérifier noms, chat, notifications et bureau avant de partager. Recorder n’envoie aucune vidéo.
