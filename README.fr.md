# FTP Sync pour Notepad++

[English](README.md) | [Русский](README.ru.md) | [Українська](README.uk.md) | [Deutsch](README.de.md) | [Español](README.es.md) | **Français** | [中文](README.zh.md)

FTP Sync est un client FTP / FTPES / SFTP pour Notepad++ : arborescence de serveurs, ouverture et enregistrement des fichiers directement sur le serveur, journal, file de transferts et protection contre l’écrasement des modifications d’autrui. Avant l’enregistrement et en arrière-plan, le plugin compare le fichier ouvert à la copie du serveur ; si le fichier a changé là-bas, il affiche les différences et propose une fusion. Rien n’est écrasé en silence, et toutes les versions des fichiers sont conservées dans des sauvegardes. Aucun autre plugin n’est nécessaire.

## Installation

1. Il faut Notepad++ (64 bits ; pour le 32 bits, prenez le zip `x86`) et .NET Framework 4.x (présent dans Windows 10/11).
2. Décompressez le zip pour obtenir `...\Notepad++\plugins\FtpSync\FtpSync.dll` (à côté de `FtpSync.Managed.dll`, `Renci.SshNet.dll` et du dossier `lang`).
3. Si Windows a marqué les fichiers comme téléchargés : clic droit sur chaque fichier → Propriétés → « Débloquer » (ou `Get-ChildItem -Recurse | Unblock-File`).
4. Redémarrez Notepad++. Menu **Modules → FTP Sync**.

Les profils peuvent être importés depuis FileZilla (XML/CSV) ou NppFTP dans les paramètres.

## Langues

L’interface est multilingue : russe (intégré), English, Українська, Deutsch, Español, Français, 中文. La langue suit celle de Windows par défaut, ou se choisit dans « Profils et paramètres » → « Langue de l’interface » (redémarrez Notepad++ après le changement).

Pour ajouter votre langue, copiez `lang\_template.txt` vers `lang\xx.txt` (xx est le code de la langue, par ex. `it`), écrivez `#name: Italiano` sur la première ligne et traduisez la colonne de droite (format `clé<TAB>traduction`, `\n` est un saut de ligne, conservez `{0}` `{1}`). Ce qui n’est pas traduit s’affiche en russe. La langue apparaît toute seule dans la liste.

## Arborescence des connexions (à droite)

Après l’installation, le panneau « FTP Sync - connexions » apparaît à droite (si vous l’avez fermé : Modules → FTP Sync → « Afficher l’arborescence des connexions », ou l’icône de la barre d’outils).

* Un arbre de profils. Double-clic sur un profil pour se connecter ; votre dossier s’ouvre aussitôt : le chemin `/ › home › … › votre dossier` sans voisins superflus, avec uniquement le contenu de votre dossier. Dossier de départ : le dernier où vous avez travaillé sur ce compte (mémorisé dans le profil), sinon celui défini dans le profil, sinon le dossier personnel du serveur. Pour voir la liste complète d’un dossier du chemin, sélectionnez-le et appuyez sur F5.
* Quand un dossier est déplié et qu’un fichier est ouvert par double-clic, l’arbre défile pour centrer l’élément choisi (verticalement et horizontalement). Si vous utilisez la molette, la barre de défilement ou une touche pendant le chargement d’un dossier, le défilement automatique ne s’en mêle pas.
* Les dossiers se chargent au fur et à mesure du dépliage. Les fichiers cachés (`.cache`, `.htaccess`) sont visibles (désactivable dans le profil).
* Des icônes sans légende distinguent les types de fichiers : PHP, JS, TS, CSS, HTML, JSON, XML, MD/TXT/LOG, images, archives, SQL, paramètres (`.ini/.conf/.htaccess/.env/.yml`), scripts (`.sh/.bat`), PDF, audio/vidéo. Les mêmes icônes servent dans l’arbre des sauvegardes et la liste d’état des fichiers.
* Double-clic sur un fichier : il est téléchargé dans le cache local et ouvert dans un onglet. Si la copie locale diffère et n’a jamais été envoyée, le plugin demande quoi faire.
* À l’enregistrement d’un fichier du cache local, il est envoyé automatiquement sur le serveur (désactivable dans le profil). Avant l’écriture, l’ancienne version du serveur va dans les sauvegardes.
* Clic droit : ouvrir, actualiser (F5), envoyer des fichiers, télécharger le dossier dans le cache, nouveau dossier/fichier, renommer (F2), supprimer (Suppr ; les copies des fichiers supprimés sont conservées), copier le chemin, « Sauvegardes de ce fichier ».
* Glissez des fichiers ou dossiers depuis l’Explorateur sur un dossier de l’arbre pour les envoyer.
* Barre de chemin au-dessus de l’arbre : saisissez un chemin et appuyez sur Entrée pour y aller.
* En bas, une courte ligne d’état : le transfert en cours avec son pourcentage (`⬆ main.css 45%` pour l’envoi, `⬇` pour le téléchargement, plus une fine barre), `✔ terminé` ou `✖ Erreur d’envoi/de téléchargement/de connexion : …`. Un clic sur la ligne ou le bouton `≡` ouvre le journal complet. Le bouton `■` arrête la file.

## Journal

Panneau du bas, onglet « Journal » : couleurs des entrées : **vert** - terminé, **orange** - envoi, **rouge** - erreur, brun - avertissement, noir - message ordinaire. Un filtre par couleur existe. Tous les événements sont consignés (connexions, téléchargements, envois, vérifications, erreurs). La dernière ligne est toujours visible (défilement automatique). Les lignes avec `▸` contiennent des détails : sélectionnez la ligne et appuyez sur « Développer » (ou double-clic) pour lire le texte complet de l’erreur ; « Copier » place l’entrée avec ses détails dans le presse-papiers. L’onglet « Journal » est un journal simple sans boutons ; l’onglet « Journal détaillé » est le même journal avec des boutons (développer, copier, effacer, fichier journal), un filtre par couleur, une recherche et le défilement automatique. Le journal complet est écrit dans `plugins\Config\FtpSync\log.txt` (bouton « Fichier journal »).

## Fonctionnalités

| Fonction | Fonctionnement |
|---|---|
| Avertissement à l’enregistrement | Avant d’écrire le fichier, compare le serveur à ce que vous avez chargé. Si le serveur a changé, une fenêtre avec les différences : **fusion automatique**, **prendre la version du serveur** (votre texte va dans un nouvel onglet et dans les sauvegardes), **écraser le serveur**. Fermer la fenêtre est le choix sûr. |
| Vérification en arrière-plan | À l’ouverture d’un fichier, au changement d’onglet, au retour dans la fenêtre de Notepad++ et par minuterie (60 s par défaut). Affiche la fenêtre « fichier modifié sur le serveur » avec les différences. |
| Trois versions d’un fichier | Conserve la « base » (ce que vous avez chargé), sait donc qui a changé quoi et sait faire une fusion à trois voies (diff3). |
| Sauvegardes selon le même chemin | Chaque version vue par le plugin (serveur, la vôtre à l’enregistrement, avant écrasement, avant envoi) est copiée dans `…\FtpSync\Backups\<profil>\<chemin sur le serveur>\<fichier>\<date_raison>.<extension>`. L’arbre du panneau suit la structure du site. Un contenu identique n’est pas dupliqué. |
| Panneau | Modules → FTP Sync → « Afficher le panneau » : onglets « État des fichiers » et « Sauvegardes » (arbre, filtre, « fichier actuel »). Le bouton « Vider les sauvegardes… » supprime toutes les sauvegardes, celles de plus de 30 jours, ou tout sauf les 3 dernières versions de chaque fichier ; « Dossier des sauvegardes » ouvre le dossier dans l’Explorateur ; le menu contextuel d’un dossier de l’arbre propose « Ouvrir le dossier » et « Vider ce dossier… ». Par version : ouvrir la copie, comparer au serveur / à l’éditeur, restaurer dans l’éditeur, envoyer sur le serveur, afficher dans l’Explorateur, supprimer. |
| Import depuis NppFTP | Facultatif : les profils, chemins de cache et correspondances de dossiers sont lus dans `NppFTP.xml`. Le mot de passe est déchiffré (NppFTP le chiffre en DES avec la clé par défaut `NppFTP00`) ; en cas d’échec, saisissez-le manuellement. Les mots de passe sont stockés avec Windows DPAPI. |
| Import depuis FileZilla | Menu « Importer les profils de FileZilla (XML/CSV)… » (ou le bouton des paramètres). Il propose par défaut `%APPDATA%\FileZilla\sitemanager.xml` (ou un fichier de « Fichier → Exporter » de FileZilla). Depuis le XML tout est repris : dossiers du Gestionnaire de sites (comme groupes de l’arbre), hôte, port, protocole (FTP, FTPES, SFTP ; HTTP/HTTPS ignorés), utilisateur, mot de passe (base64), fichier de clé SFTP, mode passif/actif, commentaire, dossiers local et distant, favoris (dans le menu « Favoris » du profil et la correspondance de dossiers). Les mots de passe protégés par le mot de passe principal de FileZilla ne peuvent pas être importés - saisissez-les manuellement (un avertissement s’affiche). Le CSV est aussi accepté. Les mots de passe sont chiffrés avec Windows DPAPI et liés à votre compte Windows. Supprimez le fichier contenant les mots de passe en clair après l’import. |
| Envoyer un fichier | « Envoyer le fichier actuel sur le serveur (avec vérification) » pour tout fichier couvert par une correspondance de dossiers. |
| Diagnostic | « À propos du plugin / diagnostic » indique à quel profil et chemin serveur appartient le fichier actuel. |

Protocoles : FTP, FTPES (TLS explicite), SFTP (mot de passe ou clé ; la connexion SFTP reste ouverte et est plus rapide que FTP). Le FTPS implicite (port 990) n’est pas pris en charge.

## Comment le plugin détermine à quel serveur appartient un fichier

* Les fichiers ouverts depuis l’arbre se trouvent dans le cache local du profil, et le plugin connaît leur chemin sur le serveur.
* Pour les fichiers hors du cache (par exemple votre propre dossier de site), ajoutez une correspondance « dossier local - dossier du serveur » dans « Profils et paramètres ». Si le diagnostic indique que le fichier « n’appartient à aucun profil », ajoutez une telle correspondance.
* La comparaison ignore les différences de fins de ligne et de BOM.
* Si NppFTP est aussi installé, désactivez l’envoi à l’enregistrement dans son profil, ou « Envoyer sur le serveur à l’enregistrement » dans le profil FTP Sync : sinon les deux plugins enverront le fichier.

## Compilation

Linux : `mono-mcs`, `mono-devel`, `mingw-w64`, `g++-mingw-w64-i686`, `zip`. `./build.sh` exécute les tests du noyau (diff/merge, import NppFTP / FileZilla XML / CSV, fichiers de langue, sauvegardes, logique de vérification), compile la partie managée et deux shims natifs (x64/x86) et dépose les archives (avec le dossier `lang`) dans `dist/`.

Conception : une petite DLL native (`native/FtpSync.cpp`) exporte les fonctions de Notepad++ et héberge .NET Framework 4 ; toute la logique et l’interface (WinForms) sont dans `FtpSync.Managed.dll`.

## État

Le noyau (comparaison, fusion, sauvegardes, import, analyse des listes FTP, file de transferts) est couvert par des tests automatiques ; les clients FTP/SFTP n’ont pas été testés avec un serveur réel. L’intégration à Notepad++ (fenêtres, ancrage, messages) suit la documentation de l’API mais n’a pas encore été exécutée dans un vrai Notepad++. Si quelque chose se comporte bizarrement, consultez `…\plugins\Config\FtpSync\log.txt` et envoyez-le.

## Auteur

Paradise Web Design Studio (@pwds), https://github.com/boardsea. Dépôt : https://github.com/boardsea/FtpSync

Le projet est sous licence MIT (voir `LICENSE`). Licence de SSH.NET (MIT) : voir `SSH.NET-LICENSE.txt`.
