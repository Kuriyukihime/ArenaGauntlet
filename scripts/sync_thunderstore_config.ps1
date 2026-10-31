$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest=Get-Content -LiteralPath (Join-Path $root 'package/manifest.json') -Raw | ConvertFrom-Json
if($manifest.name -ne 'ArenaGauntlet'){throw 'Package identity must be ArenaGauntlet.'}
function Quote-Toml([string]$Value){ConvertTo-Json -InputObject $Value -Compress}
$lines=@('# Generated from package/manifest.json. Preparation only; no upload is performed.',
    '[config]','schemaVersion = "0.0.1"','','[package]','namespace = "Kuriyukihime"',
    ('name = '+(Quote-Toml $manifest.name)),('versionNumber = '+(Quote-Toml $manifest.version_number)),
    ('description = '+(Quote-Toml $manifest.description)),('websiteUrl = '+(Quote-Toml $manifest.website_url)),
    'containsNsfwContent = false','','[package.dependencies]')
foreach($dependency in $manifest.dependencies){
    if($dependency -notmatch '^(.+)-(\d+\.\d+\.\d+)$'){throw "Invalid dependency: $dependency"}
    $lines+=(Quote-Toml $Matches[1])+' = '+(Quote-Toml $Matches[2])
}
$lines+=@('','[build]','icon = "package/icon.png"','readme = "package/README.md"','outdir = ".build/tcli-packages"')
foreach($file in @('CHANGELOG.md','LICENSE','REVIEW.md')){
    $lines+=@('','[[build.copy]]',('source = "package/'+$file+'"'),('target = "'+$file+'"'))
}
$lines+=@('','[[build.copy]]','source = "bin/Release/netstandard2.1/ArenaGauntlet.dll"',
    'target = "BepInEx/plugins/ArenaGauntlet/ArenaGauntlet.dll"','','[publish]',
    'repository = "https://thunderstore.io"','communities = ["riskofrain2"]','',
    '[publish.categories]','riskofrain2 = ["ai-generated", "maps", "gamemodes"]','')
[IO.File]::WriteAllLines((Join-Path $root 'thunderstore.toml'),[string[]]$lines,(New-Object Text.UTF8Encoding($false)))
