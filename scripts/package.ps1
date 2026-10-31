param([switch]$Release)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
$manifest = Get-Content -LiteralPath 'package/manifest.json' -Raw | ConvertFrom-Json
$version = $manifest.version_number
python scripts/build_castle_models.py
if ($LASTEXITCODE -ne 0) { throw 'Castle model generation failed.' }
python scripts/build_colosseum.py
if ($LASTEXITCODE -ne 0) { throw 'Colosseum generation failed.' }
python scripts/build_commander_insignia.py
if ($LASTEXITCODE -ne 0) { throw 'Commander insignia generation failed.' }
dotnet build BazaarGauntlet.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
dotnet run --project scripts/checks/Checks.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production rules failed.' }
dotnet run --project scripts/contracts/Contracts.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native contracts failed.' }
dotnet run --project scripts/wire/Wire.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native network wire checks failed.' }
python scripts/check_battlefield_assets.py
if ($LASTEXITCODE -ne 0) { throw 'Castle map/material asset checks failed.' }
python scripts/check_reaper_assets.py
if ($LASTEXITCODE -ne 0) { throw 'Native Harvester compatibility checks failed.' }
python scripts/check_arena_art.py
if ($LASTEXITCODE -ne 0) { throw 'Greek arena materials and geometry audit failed.' }
python scripts/check_merchant_assets.py
if ($LASTEXITCODE -ne 0) { throw 'Native merchant asset audit failed.' }
python scripts/check_brazier_marker.py
if ($LASTEXITCODE -ne 0) { throw 'Brazier marker projection audit failed.' }
python scripts/check_run_hud.py
if ($LASTEXITCODE -ne 0) { throw 'Native run HUD compatibility audit failed.' }
python scripts/make_icon.py
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
$dependencies = foreach ($dependency in $manifest.dependencies) {
    $parts = $dependency -split '-'
    $uri = "https://thunderstore.io/api/experimental/package/$($parts[0])/$($parts[1])/$($parts[2])/"
    $metadata = Invoke-RestMethod -Uri $uri
    if ($metadata.full_name -ne $dependency) { throw "Dependency mismatch: $dependency" }
    [pscustomobject]@{ dependency=$dependency; verified=$true; source=$uri }
}
$dependencies | ConvertTo-Json | Set-Content -LiteralPath '.build/dependency-validation.json' -Encoding utf8
& (Join-Path $PSScriptRoot 'sync_thunderstore_config.ps1')
$stage = Join-Path $repoRoot ('.build/package-' + [guid]::NewGuid().ToString('N'))
try {
New-Item -ItemType Directory -Path (Join-Path $stage 'BepInEx/plugins/ArenaGauntlet') -Force | Out-Null
Get-ChildItem -LiteralPath 'package' -File | Copy-Item -Destination $stage
$dll = Join-Path $repoRoot 'bin/Release/netstandard2.1/ArenaGauntlet.dll'
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll')
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne $version) { throw 'Assembly/manifest version mismatch.' }
$zipName=if($Release){"ArenaGauntlet-$version.zip"}else{"ArenaGauntlet-$version-Review.zip"}
$zip = Join-Path $repoRoot ('dist/'+$zipName)
New-Item -ItemType Directory -Path (Join-Path $repoRoot 'dist') -Force | Out-Null
if (Test-Path -LiteralPath $zip) { Move-Item -LiteralPath $zip -Destination ($zip -replace '\.zip$',('-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff')+'.zip')) }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    foreach ($required in @('manifest.json','README.md','CHANGELOG.md','REVIEW.md','LICENSE','icon.png','BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll')) {
        if ($names -notcontains $required) { throw "ZIP missing $required" }
    }
    if ($archive.Entries.Count -ne 7) { throw 'Unexpected package content.' }
    $entry=$archive.Entries | Where-Object { $_.FullName.Replace('\','/') -eq 'BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll' }
    $stream=$entry.Open()
    try { $packedHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
    if ($packedHash -ne (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash) { throw 'ZIP DLL mismatch.' }
} finally { $archive.Dispose() }
$receipt=[pscustomobject]@{ package=$zip; author='Kuriyukihime'; identity='Kuriyukihime-ArenaGauntlet'; version=$version; releasePrepared=[bool]$Release; bytes=(Get-Item -LiteralPath $zip).Length; sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash; published=$false; gameLaunched=$false; dependencyCount=@($dependencies).Count }
$receiptPath=if($Release){'.build/release-receipt.json'}else{'.build/review-receipt.json'}
$receipt | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
$receipt | ConvertTo-Json
} finally {
    $stageRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot '.build'))+[IO.Path]::DirectorySeparatorChar
    $resolvedStage=[IO.Path]::GetFullPath($stage)
    if(-not $resolvedStage.StartsWith($stageRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStage) -notmatch '^package-[a-f0-9]{32}$') { throw 'Refusing to clean a staging path outside this project.' }
    if(Test-Path -LiteralPath $resolvedStage){Remove-Item -LiteralPath $resolvedStage -Recurse -Force}
}
