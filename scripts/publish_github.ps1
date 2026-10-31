param([switch]$Publish)
# Uploads the prepared dist ZIP as a GitHub release of this repo. Build the ZIP
# with the project's package/release script first; this never rebuilds it.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $root
try {
    # Refresh the GitHub front page; if it changed, the clean-tree check below stops until it is committed.
    & (Join-Path $PSScriptRoot 'github_readme.ps1')
    $manifest=Get-Content -LiteralPath 'package/manifest.json' -Raw | ConvertFrom-Json
    $name=$manifest.name;$version=$manifest.version_number;$tag='v'+$version
    $zip=Join-Path $root ('dist/'+$name+'-'+$version+'.zip')
    if(-not (Test-Path -LiteralPath $zip)){throw ('Prepare the release first; missing '+$zip)}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entry={param($path)@($archive.Entries | Where-Object {$_.FullName.Replace('\','/') -eq $path})[0]}
        $reader=New-Object IO.StreamReader((& $entry 'manifest.json').Open())
        try{$packed=$reader.ReadToEnd() | ConvertFrom-Json}finally{$reader.Dispose()}
        if($packed.name -ne $name -or $packed.version_number -ne $version){throw 'Release ZIP identity or version differs from package/manifest.json.'}
        $dll=& $entry ('BepInEx/plugins/'+$name+'/'+$name+'.dll')
        if(-not $dll){throw 'Release ZIP has no plugin DLL.'}
        $copy=Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString()+'.dll')
        [IO.Compression.ZipFileExtensions]::ExtractToFile($dll,$copy)
        try{if([Reflection.AssemblyName]::GetAssemblyName($copy).Version.ToString(3) -ne $version){throw 'Packaged DLL version differs from the release version.'}}
        finally{Remove-Item -LiteralPath $copy}
    } finally {$archive.Dispose()}
    $changelog=Get-Content -LiteralPath 'package/CHANGELOG.md' -Raw -Encoding UTF8
    # The section runs from this version's heading to the next heading of the same level.
    $notes=[regex]::Match($changelog,'(?ms)^(#{1,2})[ \t]+'+[regex]::Escape($version)+'[ \t]*\r?$(.*?)(?=^\1[ \t]|\z)').Groups[2].Value.Trim()
    if(-not $notes){throw ('Add package/CHANGELOG.md notes for '+$version+' first.')}

    # The tag must point at the committed, pushed code that built this ZIP.
    if(git status --porcelain){throw 'Commit your changes first so the release tag matches the code that built it.'}
    git fetch -q origin
    if($LASTEXITCODE -ne 0){throw 'Could not reach the GitHub repo. Check the origin remote and gh auth status.'}
    $head=git rev-parse HEAD
    if((git rev-parse '@{u}') -ne $head){throw 'Push this branch first so the release tag points at published code.'}
    $repo=gh repo view --json nameWithOwner -q .nameWithOwner
    if($LASTEXITCODE -ne 0){throw 'GitHub CLI could not find this repo. Run gh auth login first.'}
    # Released versions are immutable; raise the version instead of replacing one.
    $released=@(gh release list --repo $repo --limit 1000 --json tagName -q '.[].tagName')
    if($LASTEXITCODE -ne 0){throw 'Could not read existing GitHub releases.'}
    $newest=$released | Where-Object {$_ -match '^v\d+\.\d+\.\d+$'} | ForEach-Object {[version]$_.Substring(1)} | Sort-Object -Descending | Select-Object -First 1
    if($newest -and $newest -ge [version]$version){throw ('Version '+$version+' is already released or older than '+$newest+'; raise the version first.')}
    if(git ls-remote --tags origin ('refs/tags/'+$tag)){throw ('Tag '+$tag+' already exists on GitHub.')}
    if(-not $Publish){Write-Host ('Verified '+$name+' '+$version+' for '+$repo+' at '+$head.Substring(0,7)+'. No upload requested.');return}

    $notesFile=Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString()+'.md')
    [IO.File]::WriteAllText($notesFile,$notes,(New-Object Text.UTF8Encoding($false)))
    try{gh release create $tag $zip --repo $repo --target $head --title ($name+' '+$version) --notes-file $notesFile}
    finally{Remove-Item -LiteralPath $notesFile}
    if($LASTEXITCODE -ne 0){throw 'GitHub did not confirm the release. Check the releases page before retrying.'}
    $sha=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    $check=Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
    try {
        gh release download $tag --repo $repo --pattern ([IO.Path]::GetFileName($zip)) --dir $check
        if($LASTEXITCODE -ne 0){throw 'Could not download the uploaded ZIP to verify it.'}
        if((Get-FileHash -LiteralPath (Join-Path $check ([IO.Path]::GetFileName($zip))) -Algorithm SHA256).Hash -ne $sha){throw 'The uploaded ZIP differs from the local one; delete that release and retry.'}
    } finally {if(Test-Path -LiteralPath $check){Remove-Item -LiteralPath $check -Recurse}}
    $url=gh release view $tag --repo $repo --json url -q .url
    $receipt=[ordered]@{publishedAtUtc=[DateTime]::UtcNow.ToString('o');repo=$repo;tag=$tag;commit=$head;sha256=$sha;release=$url}
    $receipt | ConvertTo-Json | Set-Content -LiteralPath ('dist/'+$name+'-'+$version+'-github.json') -Encoding utf8
    Write-Host ('Published '+$name+' '+$version+': '+$url)
} finally {Pop-Location}
