param(
    [ValidatePattern('^\d+\.\d+(\.\d+){0,2}$')][string]$Version = '2.1.2',
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '../publish'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts'),
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
foreach ($file in @('Sweetch.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory $file))) {
        throw "Published app is missing $file. Run build.ps1 first."
    }
}
if (!$Compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Compiler = $command.Source }
    else {
        $Compiler = @(
            "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
            "$env:ProgramFiles/Inno Setup 7/ISCC.exe",
            "${env:ProgramFiles(x86)}/Inno Setup 7/ISCC.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (!$Compiler) { throw 'Install Inno Setup 6 or 7 from https://jrsoftware.org/isdl.php, or pass -Compiler.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
& $Compiler "/DAppVersion=$Version" "/DPublishDir=$PublishDirectory" "/DArtifactsDir=$OutputDirectory" (Join-Path $PSScriptRoot '../installer/Sweetch.iss')
if ($LASTEXITCODE -ne 0) { throw 'Sweetch installer compilation failed.' }
$installer = Join-Path $OutputDirectory "Sweetch-$Version-Setup-win-x64.exe"
if (!(Test-Path -LiteralPath $installer)) { throw 'Installer output was not created.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $installer -Leaf)" | Out-File -Encoding ascii "$installer.sha256"
Write-Output "Installer ready: $installer"
