# FTP Sync for Notepad++

**English** | [Русский](README.ru.md)

FTP Sync is a standalone FTP / FTPES / SFTP client for Notepad++: a server tree, opening and saving files directly on the server, a journal, a transfer queue, and protection against overwriting someone else's changes. Before saving, and in the background, the plugin compares the open file with the copy on the server; if the file changed there, it shows the differences and offers a merge. Nothing is overwritten silently, and every version of a file is kept in backups. No other plugins are required.

## Installation

1. You need Notepad++ (64-bit; for 32-bit take the `x86` zip) and .NET Framework 4.x (included in Windows 10/11).
2. Unzip so that you get `...\Notepad++\plugins\FtpSync\FtpSync.dll` (next to `FtpSync.Managed.dll`, `Renci.SshNet.dll` and the `lang` folder).
3. If Windows marked the files as downloaded: right-click each file → Properties → "Unblock" (or run `Get-ChildItem -Recurse | Unblock-File`).
4. Restart Notepad++. Menu **Plugins → FTP Sync**.

Profiles can be imported from FileZilla (XML/CSV) or NppFTP in the settings.


## Languages

The interface is multilingual: Russian (built in), English, Українська, Deutsch, Español, Français, 中文. The language follows Windows by default, or choose it in "Profiles and settings" → "Interface language" (restart Notepad++ after changing).

To add your own language, copy `lang\_template.txt` to `lang\xx.txt` (xx is the language code, e.g. `it`), write `#name: Italiano` on the first line and translate the right-hand column (format `key<TAB>translation`, `\n` is a line break, keep `{0}` `{1}`). Untranslated text is shown in Russian. The language appears in the list automatically.

## Connection tree (right side)

After installation the "FTP Sync - connections" panel appears on the right (if closed: Plugins → FTP Sync → "Show connection tree", or the toolbar icon).

* A tree of profiles. Double-click a profile to connect; your own folder opens right away: the path `/ › home › … › your folder` without unrelated neighbours, with only your folder's content. Start folder: the last one you worked in on this account (remembered in the profile), otherwise the one set in the profile, otherwise the server's home folder. To see the full listing of any folder on the path, select it and press F5.
* When a folder is expanded or a file opened by double-click, the tree scrolls so that the selected item is centred (vertically and horizontally). If you use the wheel, the scroll bar or a key while a folder is loading, auto-scroll stays out of the way.
* Folders load as you expand them. Hidden files (`.cache`, `.htaccess`) are visible (can be turned off in the profile).
* Icons without captions tell file types apart: PHP, JS, TS, CSS, HTML, JSON, XML, MD/TXT/LOG, images, archives, SQL, settings (`.ini/.conf/.htaccess/.env/.yml`), scripts (`.sh/.bat`), PDF, audio/video. The same icons are used in the backup tree and the file status list.
* Double-click a file: it is downloaded to the local cache and opened in a tab. If the local copy differs and was never uploaded, the plugin asks what to do.
* When a file from the local cache is saved, it is uploaded to the server automatically (can be turned off in the profile). Before writing to the server, the previous server version goes to backups.
* Right-click: open, refresh (F5), upload files, download folder to the cache, new folder/file, rename (F2), delete (Del; copies of deleted files are kept in backups), copy path, "Backups of this file".
* Drag files or folders from Explorer onto a folder in the tree to upload them.
* Path bar above the tree: type a path and press Enter to go there.
* One short status line at the bottom: the running transfer with percentage (`⬆ main.css 45%` for upload, `⬇` for download, plus a thin progress bar), `✔ done`, or `✖ Upload/download/connection error: …`. Click the line or the `≡` button to open the full journal. The `■` button stops the queue.

## Journal

Bottom panel, "Journal" tab: record colours: **green** - done, **orange** - upload, **red** - error, brown - warning, black - plain message. There is a colour filter. All events are logged (connections, downloads, uploads, checks, errors). The last line is always visible (auto-scroll). Lines with `▸` carry details: select the line and press "Expand" (or double-click) to read the full error text; "Copy" puts the record with details on the clipboard. The "Journal" tab is a plain journal without buttons; the "Extended journal" tab is the same journal with buttons (expand, copy, clear, journal file), colour filter, search and auto-scroll. The full journal is written to `plugins\Config\FtpSync\log.txt` ("Journal file" button).

## Features

| Feature | How it works |
|---|---|
| Warning on save | Before writing the file, compares the server with what you loaded. If the server changed, a window with the differences: **auto-merge**, **take the server version** (your text goes to a new tab and to backups), **overwrite the server**. Closing the window is the safe choice. |
| Background check | When a file is opened, when you switch tabs, when you return to the Notepad++ window, and on a timer (60 s by default). Shows a "file changed on the server" window with the differences. |
| Three versions of a file | Keeps the "base" (what you loaded), so it knows who changed what and can do a three-way merge (diff3). |
| Backups mirroring the server path | Every version the plugin has seen (server, yours on save, before overwrite, before upload) is copied to `…\FtpSync\Backups\<profile>\<server path>\<file>\<date_reason>.<extension>`. The tree in the panel follows the site structure. Identical content is not duplicated. |
| Panel | Plugins → FTP Sync → "Show panel": the "Files status" and "Backups" tabs (tree, filter, "current file"). Per version: open the copy, compare with the server / the editor, restore into the editor, upload to the server, show in Explorer, delete. |
| Import from NppFTP | Optional: profiles, cache paths and folder mappings are read from `NppFTP.xml`. The password is decrypted (NppFTP encrypts it with DES and the default key `NppFTP00`); if that fails, enter it manually. Passwords are stored with Windows DPAPI. |
| Import from FileZilla | Menu "Import profiles from FileZilla (XML/CSV)…" (or the button in the settings). It suggests `%APPDATA%\FileZilla\sitemanager.xml` by default (or a file from FileZilla's "File → Export"). From XML everything is carried over: Site Manager folders (as groups in the tree), host, port, protocol (FTP, FTPES, SFTP; HTTP/HTTPS are skipped), user, password (base64), SFTP key file, passive/active mode, comment, local and remote folder, bookmarks (into the profile's "Bookmarks" menu and the folder mapping). Passwords protected by a FileZilla master password cannot be imported - enter them manually (a warning is shown). CSV is also accepted. Passwords are encrypted with Windows DPAPI and tied to your Windows account. Delete the file with plain-text passwords after the import. |
| Upload a file | "Upload the current file to the server (with check)" for any file covered by a folder mapping. |
| Diagnostics | "About / diagnostics" shows which profile and server path the current file belongs to. |

Protocols: FTP, FTPES (explicit TLS), SFTP (password or key; the SFTP connection is kept open, faster than FTP). Implicit FTPS (port 990) is not supported.

## How the plugin decides which server a file belongs to

* Files opened from the tree live in the profile's local cache, and the plugin knows their server path.
* For files outside the cache (for example your own site folder), add a "local folder - server folder" mapping in "Profiles and settings". If diagnostics says the file "does not belong to any profile", add such a mapping.
* The comparison ignores differences in line endings and BOM.
* If NppFTP is also installed, turn off upload-on-save in its profile, or turn off "Upload to the server on save" in the FTP Sync profile: otherwise both plugins will upload the file.

## Building

Linux: `mono-mcs`, `mono-devel`, `mingw-w64`, `g++-mingw-w64-i686`, `zip`. `./build.sh` runs the core tests (diff/merge, NppFTP / FileZilla XML / CSV import, language files, backups, check logic), builds the managed part and two native shims (x64/x86) and puts the archives (with the `lang` folder) into `dist/`.

Design: a small native DLL (`native/FtpSync.cpp`) exports the Notepad++ functions and hosts .NET Framework 4; all logic and the interface (WinForms) live in `FtpSync.Managed.dll`.

## Status

The core (comparison, merge, backups, import, FTP listing parsing, transfer queue) is covered by automated tests; the FTP/SFTP clients have not been tested against a live server. The Notepad++ integration (windows, docking, messages) is built from the API documentation but has not yet been run in a live Notepad++. If something behaves strangely, look at `…\plugins\Config\FtpSync\log.txt` and send it in.

## Author

Paradise Web Design Studio (@pwds), https://github.com/boardsea. Repository: https://github.com/boardsea/FtpSync

The project is licensed under MIT (see `LICENSE`). SSH.NET license (MIT): see `SSH.NET-LICENSE.txt`.
