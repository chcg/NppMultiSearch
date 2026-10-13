@echo off
setlocal
chcp 65001 >nul
title Instalar Multi-Search para Notepad++

set "NPP=%ProgramFiles%\Notepad++"
if not exist "%NPP%\notepad++.exe" set "NPP=%ProgramFiles(x86)%\Notepad++"
if not exist "%NPP%\notepad++.exe" (
    echo No se encontro Notepad++ en Archivos de programa.
    echo Copia manualmente la carpeta NppMultiSearch dentro de la carpeta "plugins" de tu Notepad++.
    pause
    exit /b 1
)

tasklist /fi "imagename eq notepad++.exe" 2>nul | find /i "notepad++.exe" >nul
if not errorlevel 1 (
    echo Cierra Notepad++ antes de instalar y vuelve a ejecutar este archivo.
    pause
    exit /b 1
)

echo Instalando en: %NPP%\plugins\NppMultiSearch
xcopy /E /I /Y "%~dp0NppMultiSearch" "%NPP%\plugins\NppMultiSearch" >nul
if errorlevel 1 (
    echo.
    echo No se pudo copiar. Si Notepad++ esta en Archivos de programa, ejecuta este archivo
    echo con clic derecho ^> "Ejecutar como administrador".
    pause
    exit /b 1
)

echo.
echo Listo. Abre Notepad++ y busca el menu: Complementos ^> Multi-Search
echo Atajo por defecto: Ctrl+Alt+M
pause
