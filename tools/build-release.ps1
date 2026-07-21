param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory = 'artifacts\release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $projectRoot 'src\RobocopySafe.Gui\RobocopySafe.Gui.csproj'
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$versionRoot = Join-Path $outputRoot "v$Version"

if (Test-Path -LiteralPath $versionRoot) {
    throw "Release output already exists; remove it explicitly before rebuilding: $versionRoot"
}

New-Item -ItemType Directory -Force -Path $versionRoot | Out-Null
$runtimeIdentifiers = @('win-x64', 'win-arm64')

foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    $packageName = "RobocopySafeGUI-v$Version-$runtimeIdentifier"
    $packageDirectory = Join-Path $versionRoot $packageName
    $publishArguments = @(
        'publish',
        $project,
        '-c', $Configuration,
        '-r', $runtimeIdentifier,
        '--self-contained', 'true',
        '-o', $packageDirectory,
        "-p:Version=$Version",
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false'
    )

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $runtimeIdentifier with exit code $LASTEXITCODE."
    }

    foreach ($file in @(
        'README.md',
        'README.zh-CN.md',
        'LICENSE',
        'THIRD_PARTY_NOTICES.md',
        'SECURITY.md',
        'CHANGELOG.md'
    )) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $packageDirectory -Force
    }

    $packageDocs = Join-Path $packageDirectory 'docs'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageDocs -Recurse -Force

    $packageIconDirectory = Join-Path $packageDirectory 'src\RobocopySafe.Gui\Assets'
    New-Item -ItemType Directory -Force -Path $packageIconDirectory | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $projectRoot 'src\RobocopySafe.Gui\Assets\RobocopySafeGUI.png') `
        -Destination $packageIconDirectory `
        -Force

    $executable = Join-Path $packageDirectory 'RobocopySafeGUI.exe'
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Published executable is missing: $executable"
    }

    $archive = Join-Path $versionRoot "$packageName.zip"
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archive -CompressionLevel Optimal
}

$checksumLines = Get-ChildItem -LiteralPath $versionRoot -Filter '*.zip' -File |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
$checksumPath = Join-Path $versionRoot 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

Get-ChildItem -LiteralPath $versionRoot -File |
    Select-Object Name, Length, @{ Name = 'SHA256'; Expression = {
        (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    } }
