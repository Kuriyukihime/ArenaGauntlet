param([switch]$Publish)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $root
try {
    $receipt=Get-Content -LiteralPath '.build/release-receipt.json' -Raw | ConvertFrom-Json
    $manifest=Get-Content -LiteralPath 'package/manifest.json' -Raw | ConvertFrom-Json
    $zip=Join-Path $root ('dist/ArenaGauntlet-'+$manifest.version_number+'.zip')
    $namespace=if($receipt.publishingNamespace){$receipt.publishingNamespace}else{$receipt.author}
    if($manifest.name -ne 'ArenaGauntlet' -or $namespace -ne 'Kuriyukihime' -or $receipt.version -ne $manifest.version_number){throw 'Prepared release identity does not match.'}
    if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $receipt.sha256){throw 'Prepared release ZIP hash changed.'}
    & (Join-Path $PSScriptRoot 'sync_thunderstore_config.ps1')
    $categories=Invoke-RestMethod -Uri 'https://thunderstore.io/api/experimental/community/riskofrain2/category/' -TimeoutSec 40
    foreach($slug in @('ai-generated','maps','gamemodes')){
        if(-not ($categories.results | Where-Object {$_.slug -eq $slug})){throw ('Unknown release category: '+$slug)}
    }
    $api='https://thunderstore.io/api/experimental/package/Kuriyukihime/ArenaGauntlet/'
    try {
        $listing=Invoke-RestMethod -Uri ($api+'?verify='+[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -TimeoutSec 40
        if([version]$listing.latest.version_number -ge [version]$manifest.version_number){throw 'This version is already published or older than the listing; do not upload it again.'}
    } catch {if([int]$_.Exception.Response.StatusCode -ne 404){throw}}
    if(-not $Publish){Write-Host 'Verified prepared ArenaGauntlet release, Kuriyukihime team and three category slugs. No upload requested.';return}
    dotnet tool restore
    if($LASTEXITCODE -ne 0){throw 'Could not restore the pinned publishing CLI.'}
    $previousToken=$env:TCLI_AUTH_TOKEN
    $loadedToken=$null
    try {
        if([string]::IsNullOrWhiteSpace($env:TCLI_AUTH_TOKEN)){
            # The existing protected credential belongs to the same publishing
            # team. It is never copied into this project or passed as an argument.
            $credentialPath=Join-Path $env:APPDATA 'IvoryBastionPublishing/thunderstore-token.xml'
            $loadedToken=Import-Clixml -LiteralPath $credentialPath
            if($loadedToken -isnot [Security.SecureString]){throw 'Protected publishing credential is invalid.'}
            $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($loadedToken)
            try{$env:TCLI_AUTH_TOKEN=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)}
            finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)}
        }
        dotnet tool run tcli -- publish --config-path thunderstore.toml --file $zip
        if($LASTEXITCODE -ne 0){throw 'Thunderstore did not confirm publication. Check the listing before retrying.'}
    } finally {
        $env:TCLI_AUTH_TOKEN=$previousToken
        if($loadedToken){$loadedToken.Dispose()}
    }
    $upload=[ordered]@{publishedAtUtc=[DateTime]::UtcNow.ToString('o');package='Kuriyukihime-ArenaGauntlet';version=$manifest.version_number;community='riskofrain2';categories=@('ai-generated','maps','gamemodes');sha256=$receipt.sha256;listing='https://thunderstore.io/c/riskofrain2/p/Kuriyukihime/ArenaGauntlet/'}
    $upload | ConvertTo-Json | Set-Content -LiteralPath ('dist/ArenaGauntlet-'+$manifest.version_number+'-upload.json') -Encoding utf8
} finally {Pop-Location}
