# FTP Sync für Notepad++

[English](README.md) | [Русский](README.ru.md) | [Українська](README.uk.md) | **Deutsch** | [Español](README.es.md) | [Français](README.fr.md) | [中文](README.zh.md)

[![CloudTips](https://img.shields.io/badge/CloudTips-Autor%20unterst%C3%BCtzen-00C2A8)](https://pay.cloudtips.ru/p/a4b187cc) [![YooMoney](https://img.shields.io/badge/YooMoney-Autor%20unterst%C3%BCtzen-8B3FFD)](https://yoomoney.ru/to/410015317755090)

FTP Sync ist ein FTP-/FTPES-/SFTP-Client für Notepad++: Serverbaum, Dateien direkt auf dem Server öffnen und speichern, Protokoll, Übertragungswarteschlange und Schutz davor, fremde Änderungen zu überschreiben. Vor dem Speichern und im Hintergrund vergleicht das Plugin die geöffnete Datei mit der Kopie auf dem Server; hat sich die Datei dort geändert, zeigt es die Unterschiede und bietet eine Zusammenführung an. Nichts wird stillschweigend überschrieben, und jede Version einer Datei landet in den Backups. Andere Plugins sind nicht nötig.

## Installation

1. Sie benötigen Notepad++ (64 Bit; für 32 Bit die `x86`-ZIP) und .NET Framework 4.x (in Windows 10/11 enthalten).
2. Entpacken Sie die ZIP so, dass `...\Notepad++\plugins\FtpSync\FtpSync.dll` entsteht (daneben `FtpSync.Managed.dll`, `Renci.SshNet.dll` und der Ordner `lang`).
3. Falls Windows die Dateien als heruntergeladen markiert hat: Rechtsklick auf jede Datei → Eigenschaften → „Zulassen“ (oder `Get-ChildItem -Recurse | Unblock-File`).
4. Starten Sie Notepad++ neu. Menü **Plugins → FTP Sync**.

Profile lassen sich in den Einstellungen aus FileZilla (XML/CSV) oder NppFTP importieren.

## Sprachen

Die Oberfläche ist mehrsprachig: Russisch (eingebaut), English, Українська, Deutsch, Español, Français, 中文. Die Sprache folgt standardmäßig Windows oder wird unter „Profile und Einstellungen“ → „Oberflächensprache“ gewählt (danach Notepad++ neu starten).

Eigene Sprache hinzufügen: Kopieren Sie `lang\_template.txt` nach `lang\xx.txt` (xx = Sprachcode, z. B. `it`), schreiben Sie `#name: Italiano` in die erste Zeile und übersetzen Sie die rechte Spalte (Format `Schlüssel<TAB>Übersetzung`, `\n` ist ein Zeilenumbruch, `{0}` `{1}` beibehalten). Unübersetztes erscheint auf Russisch. Die Sprache erscheint automatisch in der Liste.

## Verbindungsbaum (rechts)

Nach der Installation erscheint rechts das Fenster „FTP Sync - Verbindungen“ (falls geschlossen: Plugins → FTP Sync → „Verbindungsbaum anzeigen“ oder das Symbol in der Symbolleiste).

* Ein Baum der Profile. Doppelklick auf ein Profil verbindet; Ihr Ordner öffnet sich sofort: der Pfad `/ › home › … › Ihr Ordner` ohne unbeteiligte Nachbarn, nur mit dem Inhalt Ihres Ordners. Startordner: der zuletzt auf diesem Konto verwendete (im Profil gemerkt), sonst der im Profil eingestellte, sonst das Home-Verzeichnis des Servers. Um die vollständige Liste eines Ordners auf dem Pfad zu sehen, wählen Sie ihn aus und drücken F5.
* Beim Aufklappen eines Ordners und beim Öffnen einer Datei per Doppelklick scrollt der Baum so, dass das gewählte Element in der Mitte liegt (vertikal und horizontal). Benutzen Sie Mausrad, Scrollleiste oder eine Taste, während ein Ordner lädt, hält sich das automatische Scrollen zurück.
* Ordner werden beim Aufklappen geladen. Versteckte Dateien (`.cache`, `.htaccess`) sind sichtbar (im Profil abschaltbar).
* Symbole ohne Beschriftung unterscheiden Dateitypen: PHP, JS, TS, CSS, HTML, JSON, XML, MD/TXT/LOG, Bilder, Archive, SQL, Einstellungen (`.ini/.conf/.htaccess/.env/.yml`), Skripte (`.sh/.bat`), PDF, Audio/Video. Dieselben Symbole erscheinen im Backup-Baum und in der Dateistatus-Liste.
* Doppelklick auf eine Datei: Sie wird in den lokalen Cache geladen und in einem Tab geöffnet. Weicht die lokale Kopie ab und wurde nie hochgeladen, fragt das Plugin, was zu tun ist.
* Beim Speichern einer Datei aus dem lokalen Cache wird sie automatisch auf den Server hochgeladen (im Profil abschaltbar). Vor dem Schreiben landet die bisherige Serverversion in den Backups.
* Rechtsklick: öffnen, aktualisieren (F5), Dateien hochladen, Ordner in den Cache laden, neuer Ordner/neue Datei, umbenennen (F2), löschen (Entf; Kopien gelöschter Dateien kommen in die Backups), Pfad kopieren, „Backups dieser Datei“.
* Ziehen Sie Dateien oder Ordner aus dem Explorer auf einen Ordner im Baum, um sie hochzuladen.
* Pfadleiste über dem Baum: Pfad eingeben und Enter drücken, um dorthin zu springen.
* Unten eine kurze Statuszeile: die laufende Übertragung mit Prozent (`⬆ main.css 45%` beim Hochladen, `⬇` beim Herunterladen, dazu ein dünner Balken), `✔ fertig` oder `✖ Fehler beim Hochladen/Herunterladen/Verbinden: …`. Klick auf die Zeile oder die Schaltfläche `≡` öffnet das vollständige Protokoll. `■` stoppt die Warteschlange.

## Protokoll

Untere Leiste, Tab „Protokoll“: Farben der Einträge: **grün** - fertig, **orange** - Upload, **rot** - Fehler, braun - Warnung, schwarz - normale Meldung. Es gibt einen Farbfilter. Alle Ereignisse werden protokolliert (Verbindungen, Downloads, Uploads, Prüfungen, Fehler). Die letzte Zeile ist immer sichtbar (automatisches Scrollen). Zeilen mit `▸` enthalten Details: Zeile wählen und „Aufklappen“ drücken (oder Doppelklick), um den vollständigen Fehlertext zu lesen; „Kopieren“ legt den Eintrag mit Details in die Zwischenablage. Der Tab „Protokoll“ ist ein einfaches Protokoll ohne Schaltflächen; der Tab „Erweitertes Protokoll“ ist dasselbe Protokoll mit Schaltflächen (aufklappen, kopieren, leeren, Protokolldatei), Farbfilter, Suche und automatischem Scrollen. Das vollständige Protokoll wird in `plugins\Config\FtpSync\log.txt` geschrieben (Schaltfläche „Protokolldatei“).

## Funktionen

| Funktion | Wie sie arbeitet |
|---|---|
| Warnung beim Speichern | Vor dem Schreiben der Datei wird der Server mit dem verglichen, was Sie geladen haben. Hat sich der Server geändert, erscheint ein Fenster mit den Unterschieden: **automatisch zusammenführen**, **Serverversion übernehmen** (Ihr Text geht in einen neuen Tab und in die Backups), **Server überschreiben**. Das Fenster zu schließen ist die sichere Wahl. |
| Hintergrundprüfung | Beim Öffnen einer Datei, beim Tab-Wechsel, bei der Rückkehr ins Notepad++-Fenster und per Timer (standardmäßig 60 s). Zeigt das Fenster „Datei auf dem Server geändert“ mit den Unterschieden. |
| Drei Versionen einer Datei | Behält die „Basis“ (was Sie geladen haben), weiß daher, wer was geändert hat, und kann eine Drei-Wege-Zusammenführung (diff3). |
| Backups entlang des Serverpfads | Jede gesehene Version (Server, Ihre beim Speichern, vor dem Überschreiben, vor dem Upload) wird nach `…\FtpSync\Backups\<Profil>\<Serverpfad>\<Datei>\<Datum_Grund>.<Endung>` kopiert. Der Baum im Fenster folgt der Struktur der Website. Identischer Inhalt wird nicht doppelt gespeichert. |
| Fenster | Plugins → FTP Sync → „Fenster anzeigen“: die Tabs „Dateistatus“ und „Backups“ (Baum, Filter, „aktuelle Datei“). Die Schaltfläche „Backups leeren…“ löscht alle Backups, die älter als 30 Tage sind, oder alles außer den 3 neuesten Versionen jeder Datei; „Backup-Ordner“ öffnet den Ordner im Explorer; im Kontextmenü eines Ordners im Baum gibt es „Ordner öffnen“ und „Diesen Ordner leeren…“. Pro Version: Kopie öffnen, mit Server/Editor vergleichen, im Editor wiederherstellen, auf den Server hochladen, im Explorer zeigen, löschen. |
| Import aus NppFTP | Optional: Profile, Cache-Pfade und Ordnerzuordnungen werden aus `NppFTP.xml` gelesen. Das Passwort wird entschlüsselt (NppFTP verschlüsselt es mit DES und dem Standardschlüssel `NppFTP00`); gelingt das nicht, geben Sie es manuell ein. Passwörter werden mit Windows DPAPI gespeichert. |
| Import aus FileZilla | Menü „Profile aus FileZilla importieren (XML/CSV)…“ (oder die Schaltfläche in den Einstellungen). Vorgeschlagen wird `%APPDATA%\FileZilla\sitemanager.xml` (oder eine Datei aus „Datei → Exportieren“ in FileZilla). Aus XML wird alles übernommen: Ordner des Server-Managers (als Gruppen im Baum), Host, Port, Protokoll (FTP, FTPES, SFTP; HTTP/HTTPS werden übersprungen), Benutzer, Passwort (base64), SFTP-Schlüsseldatei, Passiv-/Aktivmodus, Kommentar, lokaler und entfernter Ordner, Lesezeichen (ins Menü „Lesezeichen“ des Profils und in die Ordnerzuordnung). Durch ein FileZilla-Master-Passwort geschützte Passwörter lassen sich nicht importieren - bitte manuell eingeben (es erscheint eine Warnung). CSV wird ebenfalls akzeptiert. Passwörter werden mit Windows DPAPI verschlüsselt und sind an Ihr Windows-Konto gebunden. Löschen Sie nach dem Import die Datei mit Klartext-Passwörtern. |
| Datei hochladen | „Aktuelle Datei auf den Server hochladen (mit Prüfung)“ für jede Datei, die in einer Ordnerzuordnung liegt. |
| Diagnose | „Über das Plugin / Diagnose“ zeigt, zu welchem Profil und Serverpfad die aktuelle Datei gehört. |

Protokolle: FTP, FTPES (explizites TLS), SFTP (Passwort oder Schlüssel; die SFTP-Verbindung bleibt offen und ist schneller als FTP). Implizites FTPS (Port 990) wird nicht unterstützt.

## Wie das Plugin erkennt, zu welchem Server eine Datei gehört

* Aus dem Baum geöffnete Dateien liegen im lokalen Cache des Profils, und das Plugin kennt ihren Serverpfad.
* Für Dateien außerhalb des Caches (z. B. Ihr eigener Website-Ordner) fügen Sie unter „Profile und Einstellungen“ eine Zuordnung „lokaler Ordner - Serverordner“ hinzu. Meldet die Diagnose, die Datei „gehört zu keinem Profil“, fügen Sie eine solche Zuordnung hinzu.
* Der Vergleich ignoriert Unterschiede bei Zeilenenden und BOM.
* Ist auch NppFTP installiert, schalten Sie in dessen Profil das Hochladen beim Speichern ab oder im FTP-Sync-Profil „Beim Speichern auf den Server hochladen“: sonst laden beide Plugins die Datei hoch.

## Bauen

Linux: `mono-mcs`, `mono-devel`, `mingw-w64`, `g++-mingw-w64-i686`, `zip`. `./build.sh` führt die Kerntests aus (diff/merge, Import aus NppFTP / FileZilla XML / CSV, Sprachdateien, Backups, Prüflogik), baut den verwalteten Teil und zwei native Shims (x64/x86) und legt die Archive (mit dem Ordner `lang`) in `dist/` ab.

Aufbau: eine kleine native DLL (`native/FtpSync.cpp`) exportiert die Notepad++-Funktionen und startet .NET Framework 4; die gesamte Logik und die Oberfläche (WinForms) liegen in `FtpSync.Managed.dll`.

## Stand

Der Kern (Vergleich, Zusammenführung, Backups, Import, Auswertung von FTP-Listen, Übertragungswarteschlange) ist durch automatische Tests abgedeckt; die FTP-/SFTP-Clients wurden nicht gegen einen Live-Server getestet. Die Notepad++-Integration (Fenster, Andocken, Nachrichten) folgt der API-Dokumentation, wurde aber noch nicht in einem echten Notepad++ ausgeführt. Verhält sich etwas seltsam, sehen Sie in `…\plugins\Config\FtpSync\log.txt` nach und senden Sie die Datei ein.

## Autor

Paradise Web Design Studio (@pwds), https://github.com/boardsea. Repository: https://github.com/boardsea/FtpSync

Das Projekt steht unter der MIT-Lizenz (siehe `LICENSE`). SSH.NET-Lizenz (MIT): siehe `SSH.NET-LICENSE.txt`.
