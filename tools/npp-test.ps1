<#
  Utilidades para probar el plugin dentro de una copia portable de Notepad++ (carpeta .test\npp, ignorada por git).
  Uso:  . .\tools\npp-test.ps1     (dot-source) y luego:  Start-TestNpp, Open-Plugin, Shot ..., Stop-TestNpp
#>
$script:Root = Split-Path $PSScriptRoot -Parent
$script:TestDir = Join-Path $script:Root '.test\npp'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Drawing; using System.Drawing.Imaging;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageW")] public static extern IntPtr SendText(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll", EntryPoint="SendMessageW")] public static extern IntPtr Send(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<object[]> Kids(IntPtr parent){
    var l=new List<object[]>();
    EnumChildWindows(parent,(h,x)=>{ RECT r; GetWindowRect(h,out r); var c=new StringBuilder(128); GetClassName(h,c,128); l.Add(new object[]{h,c.ToString(),Title(h),r.L,r.T,r.R-r.L,r.B-r.T,IsWindowVisible(h)}); return true;}, IntPtr.Zero);
    return l; }
  public static List<IntPtr> Of(uint pid){ var l=new List<IntPtr>(); EnumWindows((h,x)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid && IsWindowVisible(h)) l.Add(h); return true;}, IntPtr.Zero); return l; }
  public static string Title(IntPtr h){ var s=new StringBuilder(256); GetWindowText(h,s,256); return s.ToString(); }
  public static void Shot(IntPtr h, string path){ RECT r; GetWindowRect(h,out r); int w=r.R-r.L, ht=r.B-r.T; using(var bmp=new Bitmap(w,ht)) { using(var g=Graphics.FromImage(bmp)){ IntPtr dc=g.GetHdc(); PrintWindow(h,dc,2); g.ReleaseHdc(dc);} bmp.Save(path, ImageFormat.Png);} }
}
'@
[void][W]::SetProcessDPIAware()

function Update-TestPlugin {
    Copy-Item (Join-Path $script:Root 'dist\NppMultiSearch\NppMultiSearch.dll') (Join-Path $script:TestDir 'plugins\NppMultiSearch\') -Force
}

function Start-TestNpp([string]$File) {
    $args = @('-multiInst', '-nosession'); if ($File) { $args += $File }
    $p = Start-Process (Join-Path $script:TestDir 'notepad++.exe') -ArgumentList $args -PassThru
    Start-Sleep -Seconds 4
    $script:NppPid = [uint32]$p.Id
    $p
}

function Stop-TestNpp {
    # Solo cierra la instancia de PRUEBA (por PID), nunca otras.
    if ($script:NppPid) { Stop-Process -Id $script:NppPid -Force -ErrorAction SilentlyContinue; $script:NppPid = $null }
}

function Get-NppMain { [W]::Of($script:NppPid) | Where-Object { [W]::Title($_) -like '*Notepad++' } | Select-Object -First 1 }
function Get-PluginWindow { [W]::Of($script:NppPid) | Where-Object { [W]::Title($_) -like 'Multi-Search*' } | Select-Object -First 1 }

function Open-Plugin {
    [void][W]::PostMessage((Get-NppMain), 0x111, [IntPtr]22000, [IntPtr]::Zero)   # WM_COMMAND, ID_PLUGINS_CMD
    Start-Sleep -Seconds 2
    Get-PluginWindow
}

function Get-Kids([IntPtr]$Hwnd) {
    [W]::Kids($Hwnd) | ForEach-Object { [pscustomobject]@{ Hwnd=[IntPtr]$_[0]; Class=$_[1]; Text=$_[2]; X=$_[3]; Y=$_[4]; W=$_[5]; H=$_[6]; Visible=$_[7] } }
}
# Campos de texto de la ventana, ordenados de arriba a abajo: [carpeta, filtros, termino1.., grupo]
function Get-Edits([IntPtr]$Hwnd) { Get-Kids $Hwnd | Where-Object { $_.Class -like '*EDIT*' } | Sort-Object Y, X }
function Set-Text([IntPtr]$Hwnd, [string]$Text) { [void][W]::SendText($Hwnd, 0x000C, [IntPtr]::Zero, $Text) }   # WM_SETTEXT
function Click-Button([IntPtr]$Hwnd) { [void][W]::Send($Hwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }          # BM_CLICK
function Find-Button([IntPtr]$Hwnd, [string]$Text) { Get-Kids $Hwnd | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $Text } | Select-Object -First 1 }
function Shot([IntPtr]$Hwnd, [string]$Name) {
    $path = Join-Path $script:Root ".test\$Name.png"
    [W]::Shot($Hwnd, $path)
    $path
}

