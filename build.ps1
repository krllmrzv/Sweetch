# The version goes into Sweetch.exe; the updater compares it with the latest GitHub release.
param([ValidatePattern('^\d+\.\d+(\.\d+)?$')][string]$Version = '2.1.2')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Use a .NET 10 SDK even when an older one comes first on PATH: try the usual install places.
    $candidates = @(
        "$PSScriptRoot\.dotnet\dotnet.exe",
        (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet10\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
    $dotnet = $candidates | Where-Object { (& $_ --list-sdks 2>$null) -match '^10\.' } | Select-Object -First 1
    if (-not $dotnet) {
        # None installed: fetch one into .dotnet next to this script with Microsoft's install script (no admin needed).
        Write-Output 'Скачиваю .NET SDK 10 в папку .dotnet…'
        $installer = Join-Path ([IO.Path]::GetTempPath()) 'dotnet-install.ps1'
        Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $installer -UseBasicParsing
        & $installer -Channel 10.0 -InstallDir "$PSScriptRoot\.dotnet" -NoPath
        $dotnet = "$PSScriptRoot\.dotnet\dotnet.exe"
        if (-not ((& $dotnet --list-sdks 2>$null) -match '^10\.')) { throw 'Не удалось установить .NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0' }
    }
    $env:DOTNET_ROOT = Split-Path $dotnet

    # From scratch, so files an older build left behind (such as tools/pandoc.exe) don't ship. A running copy
    # locks part of the folder, and deleting the rest would break it: stop before touching anything.
    if (Get-Process Sweetch -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$PSScriptRoot\publish\*" }) {
        throw 'Sweetch запущен из папки publish: закройте его (значок в трее → Выход) и запустите сборку снова.'
    }
    # Empty the folder rather than delete it: a terminal opened in it ("Open terminal" on the wheel) would block that.
    if (Test-Path publish) { Get-ChildItem publish -Force | Remove-Item -Recurse -Force }
    & $dotnet publish src/Sweetch.App/Sweetch.App.csproj -c Release -r win-x64 --self-contained -p:Version=$Version -o publish
    if ($LASTEXITCODE -ne 0) { throw 'Сборка Sweetch завершилась с ошибкой.' }
    Copy-Item LICENSE, THIRD-PARTY-NOTICES.md, README.md publish/
    Write-Output 'Готово: publish/Sweetch.exe'
}
finally { Pop-Location }
