[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CoreDirectory,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$endpoint = $null
if (-not [Uri]::TryCreate($env:FIREFLY_CLIENT_API_URL, [UriKind]::Absolute, [ref]$endpoint) -or
    $endpoint.Scheme -ne 'https' -or $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment) {
    throw 'Set FIREFLY_CLIENT_API_URL to the authorized HTTPS backend URL before building.'
}
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$name = 'FireflyVPN-Palette-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$output = Join-Path $artifacts $name
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
$coreSource = (Resolve-Path -LiteralPath $CoreDirectory).Path
$required = @('xray\xray.exe', 'xray\wintun.dll', 'sing_box\sing-box.exe', 'sing_box\libcronet.dll',
    'mihomo\mihomo.exe', 'geoip.dat', 'geosite.dat')
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $coreSource $file) -PathType Leaf)) { throw "Missing core file: $file" }
}
& $Dotnet publish (Join-Path $repo 'Firefly\Firefly.Desktop\Firefly.Desktop.csproj') `
    -c Release -r win-x64 --self-contained true -p:PaletteBuild=true -p:NuGetAudit=false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
    --nologo -v:minimal -o $output
if ($LASTEXITCODE -ne 0) { throw 'Palette publish failed.' }
Rename-Item -LiteralPath (Join-Path $output 'Firefly.exe') -NewName 'FireflyVPN-Palette.exe'
foreach ($file in $required) {
    $destination = Join-Path (Join-Path $output 'bin') $file
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $coreSource $file) -Destination $destination
    if ((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath (Join-Path $coreSource $file)).Hash) {
        throw "Core copy verification failed: $file"
    }
}
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $output
@'
FireflyVPN-Palette — local favorites build

Star a node to save it. Use the Favorites filter or right-click to edit its alias.
The original subscription name follows the alias on the same line.
Changed connection settings require review; missing nodes remain bookmarked.

This is a separate portable build. It does not migrate the installed VPN's data.
When local-app-data mode is enabled, it uses %LOCALAPPDATA%\FireflyVPN-Palette.
Otherwise its settings are kept beside this executable.

The app starts proxy services when launched. Close the other VPN when you are
ready to switch, then launch FireflyVPN-Palette.exe yourself.

Source: https://github.com/chenjiakun1995-max/FireflyVPN-Palette/tree/palette
Upstream: https://github.com/Iskongkongyo/FireflyVPN-Desktop
License: GPL-3.0 (see LICENSE)
'@ | Set-Content -LiteralPath (Join-Path $output 'README.txt') -Encoding utf8
$zip = Join-Path $artifacts ($name + '.zip')
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
[pscustomobject]@{ Folder = $output; Archive = $zip; Sha256 = (Get-FileHash -LiteralPath $zip).Hash }
