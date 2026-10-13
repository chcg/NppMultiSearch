# Multi-Search para Notepad++

Plugin de Notepad++ (64 bits) para buscar **varias palabras que coincidan en una misma línea** — debe contener / no debe contener / alguno de —, en el documento actual o en una carpeta de trazas, con panel de resultados, salto a la línea y conteo de valores.

Para usuarios: ver `installer\LEEME.txt` (se incluye en el paquete).

## Compilar

Requisitos: .NET SDK (probado con 10.0) y Windows (usa el MSBuild de .NET Framework 4 que trae el sistema). **No hace falta Visual Studio.**

```powershell
.\build.ps1
```

Genera `dist\NppMultiSearch\NppMultiSearch.dll`, `dist\instalar.bat` y `dist\NppMultiSearch.zip` (lo que se reparte al equipo).

### Por qué la compilación tiene dos pasos

Notepad++ carga plugins **nativos**: la DLL debe exportar `isUnicode`, `setInfo`, `getName`, `getFuncsArray`, `beNotified` y `messageProc`. C# no exporta funciones nativas por sí solo, así que se usa [DllExport](https://github.com/3F/DllExport) 1.7.4 (paquete NuGet):

1. `dotnet build` compila el código administrado (.NET Framework 4.8, x64).
2. `tools\export.proj`, con el `MSBuild.exe` de .NET Framework 4, reescribe la DLL para exportar los métodos marcados con `[DllExport]`. La tarea de DllExport no puede ejecutarse dentro del MSBuild de `dotnet`.

`src\Plugin\DllExportAlias.cs` y `tools\DllExport.ns.props` contienen el espacio de nombres ofuscado del atributo de `DllExport.dll`; son fijos mientras se use la versión 1.7.4.

## Estructura

| Ruta | Contenido |
|---|---|
| `src\Core` | Motor sin dependencias de Notepad++: `LineQuery` (AND/NOT/OR sobre una línea), `FileSearcher` (carpetas y texto en memoria), `GroupCounter` (conteo por valor). |
| `src\Plugin` | `Exports.cs` (las 6 funciones nativas), `PluginMain.cs` (menú, atajo, hilo de interfaz), `NppInterop.cs` (mensajes de Notepad++/Scintilla), `SearchForm.cs` (ventana), `Settings.cs` (preferencias). |
| `tools\SearchCli` | Consola para probar el motor contra logs sin abrir Notepad++. |
| `tools\npp-test.ps1` | Utilidades para probar el plugin en una copia portable de Notepad++ (`.test\`, ignorada por git). |
| `installer` | `instalar.bat` y `LEEME.txt` que se empaquetan. |

## Decisiones a tener presentes

- **La ventana corre en su propio hilo con bucle de mensajes** (`PluginMain.RunUi`). Notepad++ no ejecuta el bucle de WinForms; sin esto no funcionan Tab, Enter ni los atajos de los controles. Hacia Notepad++ solo se llama con `SendMessage`, que es válido entre hilos.
- Ningún método exportado debe lanzar excepciones hacia el código nativo (cerraría Notepad++): `Exports.cs` captura todo.
- El delegado de cada comando de menú se guarda en `PluginMain.KeepAlive`; si se recolectara, Notepad++ llamaría a un puntero muerto.
- Búsqueda en carpeta: archivos leídos en streaming como UTF-8 (con detección de BOM) y abiertos en modo compartido, de modo que se pueden analizar logs que el servidor sigue escribiendo. Búsqueda en documento: se lee el texto de Scintilla con la codificación del documento.
- Se muestran como máximo 100.000 líneas; el conteo por valor sigue contando todas.

## Probar en una copia portable

```powershell
# una sola vez: copia portable de Notepad++ con solo este plugin
$t = '.\.test\npp'
robocopy "C:\Program Files\Notepad++" $t /E /XD plugins /NFL /NDL /NJH /NJS /NP
Set-Content "$t\doLocalConf.xml" ''           # modo portable: no toca %APPDATA%
New-Item -ItemType Directory "$t\plugins\NppMultiSearch" -Force

# cada vez
.\build.ps1
. .\tools\npp-test.ps1
Update-TestPlugin; Start-TestNpp 'C:\ruta\a\un.log'; Open-Plugin
Stop-TestNpp                                   # cierra SOLO la instancia de prueba (por PID)
```

## Pendiente / ideas

- Búsqueda **por campo** (usuario, móvil, hilo…) con perfiles por tipo de traza (cada aplicación escribe sus trazas con un formato distinto). Hoy "Contar por" cubre el caso de contar valores con una regex.
- Panel acoplado a Notepad++ (hoy es una ventana flotante) y tema oscuro.
- Archivos de carpeta en ANSI (no UTF-8): las tildes se leen mal; buscar texto ASCII no se ve afectado.
- Pruebas automatizadas del motor (`LineQuery`, `GroupCounter`).

## Licencia

MIT. Ver [LICENSE](LICENSE).
