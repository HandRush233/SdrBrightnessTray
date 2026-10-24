param(
    [switch]$RegenerateIcon
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw 'The in-box .NET Framework C# compiler (csc.exe) was not found.'
}

$icon = Join-Path $root 'assets\app.ico'
if ($RegenerateIcon) {
    $python = (Get-Command python -ErrorAction SilentlyContinue).Source
    if (-not $python) { throw 'Python with Pillow is required to regenerate the icon.' }
    & $python (Join-Path $root 'tools\make_icon.py')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw 'assets\app.ico is missing.'
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
)
$sources = Get-ChildItem -Path (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName }
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/codepage:65001',
    ('/out:' + (Join-Path $dist 'SdrBrightnessTray.exe')),
    ('/win32manifest:' + (Join-Path $root 'src\app.manifest')),
    ('/win32icon:' + $icon),
    ('/resource:' + $icon + ',SdrBrightnessTray.AppIcon.ico')
)

& $csc $cscArgs $refs $sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $root 'Install.cmd') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'Uninstall.cmd') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\preview.png') -Destination $dist -Force

$exe = Get-Item -LiteralPath (Join-Path $dist 'SdrBrightnessTray.exe')
Write-Host ''
Write-Host ('Built: ' + $exe.FullName)
Write-Host ('Size : ' + $exe.Length + ' bytes')
Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash)
