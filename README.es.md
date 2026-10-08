# FTP Sync para Notepad++

[English](README.md) | [Русский](README.ru.md) | [Українська](README.uk.md) | [Deutsch](README.de.md) | **Español** | [Français](README.fr.md) | [中文](README.zh.md)

FTP Sync es un cliente FTP / FTPES / SFTP para Notepad++: árbol de servidores, abrir y guardar archivos directamente en el servidor, registro, cola de transferencias y protección contra la sobrescritura de cambios ajenos. Antes de guardar y en segundo plano, el plugin compara el archivo abierto con la copia del servidor; si el archivo cambió allí, muestra las diferencias y ofrece fusionarlas. Nada se sobrescribe en silencio, y todas las versiones de los archivos se guardan en copias de seguridad. No se necesitan otros plugins.

## Instalación

1. Necesita Notepad++ (64 bits; para 32 bits use el zip `x86`) y .NET Framework 4.x (incluido en Windows 10/11).
2. Descomprima el zip de modo que quede `...\Notepad++\plugins\FtpSync\FtpSync.dll` (junto a `FtpSync.Managed.dll`, `Renci.SshNet.dll` y la carpeta `lang`).
3. Si Windows marcó los archivos como descargados: clic derecho en cada archivo → Propiedades → «Desbloquear» (o `Get-ChildItem -Recurse | Unblock-File`).
4. Reinicie Notepad++. Menú **Plugins → FTP Sync**.

Los perfiles se pueden importar de FileZilla (XML/CSV) o de NppFTP en los ajustes.

## Idiomas

La interfaz es multilingüe: ruso (integrado), English, Українська, Deutsch, Español, Français, 中文. El idioma sigue al de Windows por defecto, o se elige en «Perfiles y ajustes» → «Idioma de la interfaz» (reinicie Notepad++ tras cambiarlo).

Para añadir su idioma, copie `lang\_template.txt` a `lang\xx.txt` (xx es el código del idioma, p. ej. `it`), escriba `#name: Italiano` en la primera línea y traduzca la columna derecha (formato `clave<TAB>traducción`, `\n` es un salto de línea, conserve `{0}` `{1}`). Lo no traducido se muestra en ruso. El idioma aparece solo en la lista.

## Árbol de conexiones (a la derecha)

Tras la instalación aparece a la derecha el panel «FTP Sync - conexiones» (si lo cerró: Plugins → FTP Sync → «Mostrar el árbol de conexiones», o el icono de la barra de herramientas).

* Un árbol de perfiles. Doble clic en un perfil para conectar; su carpeta se abre enseguida: la ruta `/ › home › … › su carpeta` sin vecinos ajenos y con solo el contenido de su carpeta. Carpeta inicial: la última en la que trabajó con esta cuenta (se recuerda en el perfil), si no la fijada en el perfil, si no la carpeta personal del servidor. Para ver el listado completo de cualquier carpeta de la ruta, selecciónela y pulse F5.
* Al expandir una carpeta y al abrir un archivo con doble clic, el árbol se desplaza para que el elemento elegido quede en el centro (vertical y horizontalmente). Si usa la rueda, la barra de desplazamiento o una tecla mientras se carga una carpeta, el desplazamiento automático no interviene.
* Las carpetas se cargan al expandirlas. Los archivos ocultos (`.cache`, `.htaccess`) son visibles (se puede desactivar en el perfil).
* Los iconos sin texto distinguen los tipos de archivo: PHP, JS, TS, CSS, HTML, JSON, XML, MD/TXT/LOG, imágenes, archivos comprimidos, SQL, ajustes (`.ini/.conf/.htaccess/.env/.yml`), scripts (`.sh/.bat`), PDF, audio/vídeo. Los mismos iconos se usan en el árbol de copias y en la lista de estado de archivos.
* Doble clic en un archivo: se descarga a la caché local y se abre en una pestaña. Si la copia local difiere y nunca se subió, el plugin pregunta qué hacer.
* Al guardar un archivo de la caché local, se sube solo al servidor (se puede desactivar en el perfil). Antes de escribir en el servidor, la versión anterior del servidor pasa a las copias.
* Clic derecho: abrir, actualizar (F5), subir archivos, descargar carpeta a la caché, nueva carpeta/archivo, renombrar (F2), eliminar (Supr; las copias de los archivos eliminados se guardan), copiar ruta, «Copias de este archivo».
* Arrastre archivos o carpetas desde el Explorador a una carpeta del árbol para subirlos.
* Barra de ruta sobre el árbol: escriba una ruta y pulse Enter para ir allí.
* Abajo hay una línea de estado corta: la transferencia en curso con porcentaje (`⬆ main.css 45%` al subir, `⬇` al descargar, más una barra fina), `✔ listo` o `✖ Error al subir/descargar/conectar: …`. Un clic en la línea o el botón `≡` abre el registro completo. El botón `■` detiene la cola.

## Registro

Panel inferior, pestaña «Registro»: colores de los registros: **verde** - listo, **naranja** - subida, **rojo** - error, marrón - advertencia, negro - mensaje normal. Hay un filtro por color. Se registran todos los eventos (conexiones, descargas, subidas, comprobaciones, errores). La última línea se ve siempre (desplazamiento automático). Las líneas con `▸` tienen detalles: seleccione la línea y pulse «Expandir» (o doble clic) para leer el texto completo del error; «Copiar» pone el registro con sus detalles en el portapapeles. La pestaña «Registro» es un registro simple sin botones; la pestaña «Registro ampliado» es el mismo registro con botones (expandir, copiar, borrar, archivo de registro), filtro por color, búsqueda y desplazamiento automático. El registro completo se escribe en `plugins\Config\FtpSync\log.txt` (botón «Archivo de registro»).

## Funciones

| Función | Cómo funciona |
|---|---|
| Aviso al guardar | Antes de escribir el archivo compara el servidor con lo que usted cargó. Si el servidor cambió, aparece una ventana con las diferencias: **fusión automática**, **tomar la del servidor** (su texto va a una pestaña nueva y a las copias), **sobrescribir el servidor**. Cerrar la ventana es la opción segura. |
| Comprobación en segundo plano | Al abrir un archivo, al cambiar de pestaña, al volver a la ventana de Notepad++ y con un temporizador (60 s por defecto). Muestra la ventana «archivo cambiado en el servidor» con las diferencias. |
| Tres versiones de un archivo | Guarda la «base» (lo que usted cargó), así que sabe quién cambió qué y puede hacer una fusión a tres bandas (diff3). |
| Copias con la misma ruta | Cada versión que el plugin ha visto (del servidor, la suya al guardar, antes de sobrescribir, antes de subir) se copia en `…\FtpSync\Backups\<perfil>\<ruta en el servidor>\<archivo>\<fecha_motivo>.<extensión>`. El árbol del panel sigue la estructura del sitio. El contenido idéntico no se duplica. |
| Panel | Plugins → FTP Sync → «Mostrar el panel»: pestañas «Estado de archivos» y «Copias» (árbol, filtro, «archivo actual»). El botón «Limpiar copias…» elimina todas las copias, las de más de 30 días, o todo salvo las 3 últimas versiones de cada archivo; «Carpeta de copias» abre la carpeta en el Explorador; el menú contextual de una carpeta del árbol tiene «Abrir carpeta» y «Limpiar esta carpeta…». Por versión: abrir la copia, comparar con el servidor / el editor, restaurar en el editor, subir al servidor, mostrar en el Explorador, eliminar. |
| Importar de NppFTP | Opcional: los perfiles, rutas de caché y correspondencias de carpetas se leen de `NppFTP.xml`. La contraseña se descifra (NppFTP la cifra con DES y la clave por defecto `NppFTP00`); si no se logra, introdúzcala a mano. Las contraseñas se guardan con Windows DPAPI. |
| Importar de FileZilla | Menú «Importar perfiles de FileZilla (XML/CSV)…» (o el botón de los ajustes). Propone por defecto `%APPDATA%\FileZilla\sitemanager.xml` (o un archivo de «Archivo → Exportar» de FileZilla). Del XML se traslada todo: carpetas del Gestor de sitios (como grupos del árbol), host, puerto, protocolo (FTP, FTPES, SFTP; HTTP/HTTPS se omiten), usuario, contraseña (base64), archivo de clave SFTP, modo pasivo/activo, comentario, carpeta local y remota, marcadores (al menú «Marcadores» del perfil y a la correspondencia de carpetas). Las contraseñas protegidas por la contraseña maestra de FileZilla no se pueden importar: introdúzcalas a mano (se muestra un aviso). También se acepta CSV. Las contraseñas se cifran con Windows DPAPI y quedan ligadas a su cuenta de Windows. Después de importar, elimine el archivo con contraseñas en texto claro. |
| Subir un archivo | «Subir el archivo actual al servidor (con comprobación)» para cualquier archivo cubierto por una correspondencia de carpetas. |
| Diagnóstico | «Acerca del plugin / diagnóstico» muestra a qué perfil y ruta del servidor pertenece el archivo actual. |

Protocolos: FTP, FTPES (TLS explícito), SFTP (contraseña o clave; la conexión SFTP se mantiene abierta y es más rápida que FTP). El FTPS implícito (puerto 990) no es compatible.

## Cómo decide el plugin a qué servidor pertenece un archivo

* Los archivos abiertos desde el árbol están en la caché local del perfil, y el plugin conoce su ruta en el servidor.
* Para archivos fuera de la caché (por ejemplo su propia carpeta del sitio), añada una correspondencia «carpeta local - carpeta del servidor» en «Perfiles y ajustes». Si el diagnóstico dice que el archivo «no pertenece a ningún perfil», añada esa correspondencia.
* La comparación ignora las diferencias en los fines de línea y el BOM.
* Si NppFTP también está instalado, desactive en su perfil la subida al guardar, o desactive «Subir al servidor al guardar» en el perfil de FTP Sync: de lo contrario ambos plugins subirán el archivo.

## Compilación

Linux: `mono-mcs`, `mono-devel`, `mingw-w64`, `g++-mingw-w64-i686`, `zip`. `./build.sh` ejecuta las pruebas del núcleo (diff/merge, importación de NppFTP / FileZilla XML / CSV, archivos de idioma, copias, lógica de comprobación), compila la parte administrada y dos shims nativos (x64/x86) y deja los archivos (con la carpeta `lang`) en `dist/`.

Diseño: una pequeña DLL nativa (`native/FtpSync.cpp`) exporta las funciones de Notepad++ y aloja .NET Framework 4; toda la lógica y la interfaz (WinForms) están en `FtpSync.Managed.dll`.

## Estado

El núcleo (comparación, fusión, copias, importación, análisis de listados FTP, cola de transferencias) está cubierto por pruebas automáticas; los clientes FTP/SFTP no se han probado con un servidor real. La integración con Notepad++ (ventanas, acoplamiento, mensajes) está hecha según la documentación de la API, pero aún no se ha ejecutado en un Notepad++ real. Si algo se comporta de forma extraña, mire `…\plugins\Config\FtpSync\log.txt` y envíelo.

## Autor

Paradise Web Design Studio (@pwds), https://github.com/boardsea. Repositorio: https://github.com/boardsea/FtpSync

El proyecto tiene licencia MIT (véase `LICENSE`). Licencia de SSH.NET (MIT): véase `SSH.NET-LICENSE.txt`.
