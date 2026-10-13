<#
.SYNOPSIS
  Compila el plugin Multi-Search para Notepad++ (64 bits) y arma dist\ listo para distribuir.

.DESCRIPTION
  1) dotnet build  -> compila la DLL administrada (.NET Framework 4.8, x64).
  2) export.proj   -> convierte los metodos [DllExport] en funciones nativas que Notepad++ puede cargar.
                      Usa el MSBuild de .NET Framework 4 que trae Windows (el de "dotnet" no puede cargar esa tarea).
  Resultado: dist\NppMultiSearch\NppMultiSearch.dll  +  dist\instalar.bat  +  dist\NppMultiSearch.zip

.EXAMPLE
  .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- dotnet (puede no estar en el PATH de la sesion) ---
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw "No se encontro dotnet. Instala el .NET SDK." }

# --- MSBuild de .NET Framework 4 ---
$msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
if (-not (Test-Path $msbuild)) { throw "No se encontro $msbuild" }

$csproj = Join-Path $root 'src\Plugin\NppMultiSearch.csproj'

Write-Host '==> 1/3 Compilando (dotnet build)...' -ForegroundColor Cyan
& $dotnet build $csproj -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion.' }

$built = Join-Path $root "src\Plugin\bin\$Configuration\net48\NppMultiSearch.dll"
if (-not (Test-Path $built)) { throw "No se genero $built" }

# --- carpeta de salida limpia ---
$dist = Join-Path $root 'dist'
$pluginDir = Join-Path $dist 'NppMultiSearch'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $pluginDir | Out-Null
$dll = Join-Path $pluginDir 'NppMultiSearch.dll'
Copy-Item $built $dll

Write-Host '==> 2/3 Exportando funciones nativas (DllExport)...' -ForegroundColor Cyan
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$pkg = Join-Path $nugetRoot 'dllexport\1.7.4'
if (-not (Test-Path $pkg)) { throw "Falta el paquete DllExport 1.7.4 en $pkg (se restaura con dotnet build)." }

$before = (Get-Item $dll).Length
& $msbuild (Join-Path $root 'tools\export.proj') /nologo /v:m "/p:InputDll=$dll" "/p:Pkg=$pkg"
if ($LASTEXITCODE -ne 0) { throw 'Fallo la exportacion de funciones nativas.' }
$after = (Get-Item $dll).Length
if ($after -eq $before) { throw 'La DLL no cambio tras la exportacion: no se exportaron funciones.' }

Write-Host '==> 3/3 Empaquetando...' -ForegroundColor Cyan
Copy-Item (Join-Path $root 'installer\instalar.bat') $dist
Copy-Item (Join-Path $root 'installer\LEEME.txt') $dist
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath (Join-Path $dist 'NppMultiSearch.zip') -Force

# --- paquete para la lista oficial de plugins (nppPluginList): la DLL va en la RAIZ del zip ---
$ver = (Get-Item $dll).VersionInfo.FileVersion            # la lista exige que coincida con la version binaria de la DLL
$releaseZip = Join-Path $dist "NppMultiSearch_v${ver}_x64.zip"
Compress-Archive -Path $dll -DestinationPath $releaseZip -Force
$sha = (Get-FileHash $releaseZip -Algorithm SHA256).Hash.ToLower()
$entry = @"
{
  "folder-name": "NppMultiSearch",
  "display-name": "Multi-Search",
  "version": "$ver",
  "id": "$sha",
  "repository": "https://github.com/NachoMarengo/NppMultiSearch/releases/download/v$($ver.Substring(0, $ver.LastIndexOf('.')))/NppMultiSearch_v${ver}_x64.zip",
  "description": "Search several words that match on the same line (must / must not / any of) in the current document or a folder, with results panel and per-value counts.",
  "author": "NachoMarengo",
  "homepage": "https://github.com/NachoMarengo/NppMultiSearch",
  "npp-compatible-versions": "[8.9.7,]"
}
"@
Set-Content (Join-Path $dist 'pl.x64.entry.json') $entry -Encoding UTF8

Write-Host ''
Write-Host "Listo: $dll ($after bytes)" -ForegroundColor Green
Write-Host "Paquete para el equipo: $(Join-Path $dist 'NppMultiSearch.zip')" -ForegroundColor Green
