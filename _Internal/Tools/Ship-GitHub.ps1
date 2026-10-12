# Publishes this checked source to the EXISTING public WorkChatMini repository.
# Does not build locally, alter the installed WorkChat app, or touch Story data.
param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# This BAT runs in an interactive terminal. Never launch Git/gh's `less`
# pager: it shows `(END)` and blocks the publish confirmation prompt.
$env:GIT_PAGER = 'cat'
$env:GH_PAGER = 'cat'
$env:PAGER = 'cat'
$root = [IO.Path]::GetFullPath($SourceRoot)
$repository = 'PHai237/WorkChatMini'
$version = (Get-Content -LiteralPath (Join-Path $root '_Internal/VERSION.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be x.y.z without a preview suffix.' }
foreach ($name in @('gh','git')) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { throw "Missing $name. Install Git and GitHub CLI first." }
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI login missing: run gh auth login first.' }
if (-not (Test-Path -LiteralPath (Join-Path $root '.github/workflows/release-windows.yml') -PathType Leaf)) {
    throw 'Run PUSH.bat only from the extracted WorkChat.zip source root.'
}
# Explicit allowlist. Never publish local .git or user data, even if placed in this folder.
$allowedRoots = @('.github','_Internal','docs','.gitignore','README.md','BUILD.bat','AUTO-TEST.bat','SOURCE-CHECK.bat','PUSH.bat')
$unexpected = @(Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -notin $allowedRoots })
if ($unexpected.Count -gt 0) { throw ('Unexpected files at source root: ' + (($unexpected | ForEach-Object Name) -join ', ')) }
$allowedExts = @('.cs','.xaml','.csproj','.md','.ps1','.bat','.txt','.json','.yml','.yaml','.ico','.png','.iss','.manifest','.gitignore')
$files = @(Get-ChildItem -LiteralPath $root -File -Force -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|Build|Releases|Logs|CrashReports)[\\/]' -and
    $_.Extension -in $allowedExts -and $_.Name -notin @('desktop.ini','Thumbs.db')
})
if ($files.Count -lt 200) { throw 'Source package looks incomplete. Publishing stopped.' }
$repoWork = Join-Path ([IO.Path]::GetTempPath()) ('WorkChatShip-' + [Guid]::NewGuid().ToString('N'))
try {
    Write-Host "Cloning $repository into a temporary directory..."
    & gh repo clone $repository $repoWork -- --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone repository. Check gh auth and permissions.' }
    $branch = (& git -C $repoWork branch --show-current).Trim()
    if ($branch -ne 'main') { throw "Expected main branch; found '$branch'." }
    $dirty = @(& git -C $repoWork status --porcelain)
    if ($dirty.Count -gt 0) { throw 'Fresh repository checkout is unexpectedly dirty.' }
    foreach ($file in $files) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Symlink / reparse point rejected: $($file.FullName)"
        }
        $rel = $file.FullName.Substring($root.TrimEnd([char]92, [char]47).Length).TrimStart([char]92, [char]47)
        if ($rel -match '(^|[\\/])\.env(?:\.|$)|(?i)\.(?:key|pem|pfx|p12|zip|exe|dll)$') {
            throw "Sensitive or binary file in source: $rel"
        }
        $target = Join-Path $repoWork $rel
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    # Remove only obsolete top-level developer BATs from the old release bridge.
    foreach ($obsolete in @('INSTALL-APP.bat','RESTORE-PREVIOUS.bat','UPDATE-NOW.bat',
                           'FIRST-AND-LAST-LOCAL-BUILD.bat','PUBLISH-GITHUB-ONCE.bat')) {
        $old = Join-Path $repoWork $obsolete
        if (Test-Path -LiteralPath $old -PathType Leaf) { Remove-Item -LiteralPath $old -Force }
    }
    & git -C $repoWork add -A
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    $staged = @(& git -C $repoWork diff --cached --name-only)
    if ($staged.Count -eq 0) {
        $checkedOutVersion = (& git -C $repoWork show 'HEAD:_Internal/VERSION.txt').Trim()
        if ($LASTEXITCODE -ne 0 -or $checkedOutVersion -ne $version) {
            throw "No staged changes but GitHub checkout version is $checkedOutVersion (expected $version)."
        }
        Write-Host "GitHub already contains the $version source. Skipping commit; you can still request the release workflow." -ForegroundColor Green
    }
    $unsafe = @($staged | Where-Object { $_ -match '(?i)(^|/)(\.env(?:\.|$)|[^/]+\.(exe|dll|zip|pfx|pem|key)|[^/]+\.secrets\.json$|Logs|CrashReports)(/|$)' })
    if ($unsafe.Count -gt 0) { throw ('Refusing sensitive/generated files: ' + ($unsafe -join ', ')) }
    Write-Host "Version: $version | Staged changes: $($staged.Count)" -ForegroundColor Cyan
    if ($staged.Count -gt 0) {
        Write-Host 'Changes to publish (pager disabled):' -ForegroundColor Cyan
        & git --no-pager -C $repoWork diff --cached --stat
        if ($LASTEXITCODE -ne 0) { throw 'Could not summarize staged changes.' }
    }
    Write-Host 'This will publish pending source changes (if any) and request a Windows GitHub build.' -ForegroundColor Yellow
    $ok = (Read-Host 'Type YES to continue').Trim()
    if ($ok -cne 'YES') { throw 'Cancelled. You must type YES in capitals. No new workflow was started.' }
    if ($staged.Count -gt 0) {
        & git -C $repoWork commit -m "WorkChat $version - publish source"
        if ($LASTEXITCODE -ne 0) { throw 'Git commit failed. Set git user.name/email once if needed.' }
        & git -C $repoWork push origin main
        if ($LASTEXITCODE -ne 0) { throw 'Git push failed. Existing release and installed WorkChat are unchanged.' }
    }
    # Verify the *remote* branch points at the newly pushed commit. Do not
    # mistake a successful local commit / queued workflow for published source.
    $localSha = (& git -C $repoWork rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or !$localSha) { throw 'Cannot resolve local pushed commit.' }
    $remoteRef = @(& git -C $repoWork ls-remote origin refs/heads/main)
    if ($LASTEXITCODE -ne 0 -or $remoteRef.Count -ne 1) { throw 'Cannot verify GitHub main after push.' }
    $remoteSha = ($remoteRef[0] -split '\s+')[0]
    if ($remoteSha -ne $localSha) {
        throw "GitHub main verification failed: remote=$remoteSha; pushed=$localSha. Do not run update yet."
    }
    # Confirm the actual version file hosted by GitHub, not merely the local copy.
    $encoded = (& gh api "repos/$repository/contents/_Internal/VERSION.txt?ref=main" --jq '.content')
    if ($LASTEXITCODE -ne 0 -or !$encoded) { throw 'GitHub version file cannot be read after push.' }
    # PowerShell binds -replace inside a method argument list as an extra argument.
    # Compute the cleaned Base64 string separately: FromBase64String takes ONE string.
    $cleanBase64 = (($encoded -join '') -replace '\s', '')
    $remoteVersion = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cleanBase64)).Trim()
    if ($remoteVersion -ne $version) {
        throw "GitHub still serves $remoteVersion but this package is $version. Publish verification FAILED."
    }
    Write-Host "VERIFIED GITHUB MAIN VERSION: $remoteVersion | commit $remoteSha" -ForegroundColor Green
    Write-Host 'Source pushed and verified. Requesting GitHub Windows build...' -ForegroundColor Green
    & gh workflow run release-windows.yml --repo $repository --ref main
    if ($LASTEXITCODE -ne 0) { throw 'Push succeeded, but starting workflow failed. Run it manually from GitHub Actions.' }
    Write-Host "DONE: GitHub MAIN SOURCE is $version, workflow requested (NOT YET BUILT)." -ForegroundColor Green
    Write-Host 'Watch https://github.com/PHai237/WorkChatMini/actions' -ForegroundColor Green
    Write-Host 'After ALL jobs pass, open WorkChat > More > Updates > Check.'
}
finally {
    if (Test-Path -LiteralPath $repoWork) {
        Remove-Item -LiteralPath $repoWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
