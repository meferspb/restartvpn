[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Dotnet = 'dotnet',
    [string]$Version = '',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    if ($Version) {
        if ($Version -notmatch '^\d+\.\d+\.\d+$') {
            throw "Version must look like 1.2.3, got '$Version'"
        }
    } else {
        [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
        $Version = @($props.Project.PropertyGroup)[0].Version
        if (-not $Version) { $Version = '0.0.0' }
    }

    Write-Host "RestartVPN $Version ($Configuration)" -ForegroundColor Cyan

    $buildArgs = @('build', (Join-Path $root 'RestartVpn.sln'), '-c', $Configuration, '--nologo')
    if ($PSBoundParameters.ContainsKey('Version') -and $Version) {
        $buildArgs += "-p:Version=$Version"
        $buildArgs += "-p:FileVersion=$($Version).0"
        $buildArgs += "-p:InformationalVersion=$Version"
    }

    & $Dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

    if (-not $SkipTests) {
        & $Dotnet test (Join-Path $root 'tests\RestartVpn.Tests\RestartVpn.Tests.csproj') -c $Configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }
    }

    $outDir = Join-Path $root "src\RestartVpn.App\bin\$Configuration\net48"
    $exe = Join-Path $outDir 'RestartVPN.exe'
    if (-not (Test-Path $exe)) { throw "Missing $exe" }

    # Release contract: exactly one exe, no side-by-side assemblies.
    $sideBySide = Get-ChildItem $outDir -Filter '*.dll' -ErrorAction SilentlyContinue
    if ($sideBySide) {
        throw "Single-file contract broken; unexpected DLLs: $($sideBySide.Name -join ', ')"
    }

    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $target = Join-Path $dist ("RestartVPN-" + $Version + ".exe")
    Copy-Item $exe $target -Force

    $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
    $name = Split-Path -Leaf $target
    Set-Content -Path ($target + '.sha256') -Value ($hash + '  ' + $name) -Encoding ASCII

    Write-Host ""
    Write-Host "Artifact: $target" -ForegroundColor Green
    Write-Host "SHA-256 : $hash" -ForegroundColor Green
}
finally {
    Pop-Location
}
