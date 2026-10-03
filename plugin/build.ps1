# Собирает IronLungRu.dll без Visual Studio: нужен только компилятор C#, который есть в каждой Windows.
# Пример: powershell -ExecutionPolicy Bypass -File plugin/build.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Iron Lung"
# В папке игры уже должен стоять русификатор (нужны BepInEx\core\BepInEx.dll и 0Harmony.dll).
param([Parameter(Mandatory = $true)][string]$GamePath)

$ErrorActionPreference = "Stop"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$managed = Join-Path $GamePath "Iron Lung_Data\Managed"
$core = Join-Path $GamePath "BepInEx\core"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "package\BepInEx\plugins\IronLungRu\IronLungRu.dll"

foreach ($p in @($csc, $managed, (Join-Path $core "BepInEx.dll"))) {
    if (-not (Test-Path $p)) { throw "Не найдено: $p" }
}

$refs = @("mscorlib.dll", "netstandard.dll", "System.dll", "System.Core.dll", "UnityEngine.dll",
          "UnityEngine.CoreModule.dll", "UnityEngine.TextCoreModule.dll", "UnityEngine.TextRenderingModule.dll",
          "UnityEngine.UI.dll", "UnityEngine.UIModule.dll", "Unity.TextMeshPro.dll", "Assembly-CSharp.dll") |
        ForEach-Object { "-r:" + (Join-Path $managed $_) }
$refs += "-r:" + (Join-Path $core "BepInEx.dll")
$refs += "-r:" + (Join-Path $core "0Harmony.dll")

& $csc -nologo -optimize+ -target:library -nostdlib+ -noconfig "-out:$out" @refs (Join-Path $PSScriptRoot "IronLungRu.cs")
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась" }
Write-Host "Готово: $out"
