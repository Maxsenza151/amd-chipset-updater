# Builds AMD Chipset Updater:
#   dist\AMDChipsetUpdater.exe                    the program
#   dist\AMDChipsetUpdater-Portable-<ver>.zip     portable version
#   dist\AMDChipsetUpdater-Setup-<ver>.exe        installer (needs Inno Setup 6)
#
# Only uses the C# compiler that ships with Windows (.NET Framework 4.x), no SDK required.
#   powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$Version = '1.0.0'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'AMDChipsetUpdater.exe'

New-Item -ItemType Directory -Force $dist | Out-Null

# 1. Icon
$ico = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $ico)) { & (Join-Path $root 'assets\make-icon.ps1') -Out $ico }

# 2. Compile
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$exe" "/win32icon:$ico" `
    "/win32manifest:$(Join-Path $root 'src\app.manifest')" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed" }
Write-Host "Built $exe"

# 3. Portable zip
$staging = Join-Path $dist 'portable'
Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item $exe $staging
Copy-Item (Join-Path $root 'assets\README-portable.txt') (Join-Path $staging 'README.txt')
$zip = Join-Path $dist "AMDChipsetUpdater-Portable-$Version.zip"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $staging
Write-Host "Built $zip"

# 4. Installer
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning "Inno Setup 6 not found: installer skipped. Install it with: winget install JRSoftware.InnoSetup"
    return
}
& $iscc /Q "/DAppVersion=$Version" (Join-Path $root 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Write-Host "Built $(Join-Path $dist "AMDChipsetUpdater-Setup-$Version.exe")"
