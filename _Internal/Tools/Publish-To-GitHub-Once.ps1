# One-time OPTIONAL publisher. Never runs without explicit user action.
# Requires GitHub CLI (gh), Git, and an already authenticated GitHub account.
param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($SourceRoot)
if (-not (Test-Path -LiteralPath (Join-Path $root '.github\workflows\release-windows.yml'))) {
    throw 'This is not the WorkChat release source folder.'
}
foreach ($tool in @('git.exe','gh.exe')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Missing $tool. Install Git for Windows and GitHub CLI (gh), then rerun."
    }
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'Sign in once using: gh auth login' }
# Do not accidentally publish arbitrary user files from an old working folder.
$allowedRoot = @('.github', '.gitignore', '_Internal', 'docs', 'README.md',
                 'AUTO-TEST.bat', 'BUILD.bat', 'INSTALL-APP.bat', 'RESTORE-PREVIOUS.bat',
                 'SOURCE-CHECK.bat', 'UPDATE-NOW.bat', 'PUBLISH-GITHUB-ONCE.bat',
                 'FIRST-AND-LAST-LOCAL-BUILD.bat', '.git')
$unexpected = @(Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -notin $allowedRoot })
if ($unexpected.Count -gt 0) {
    throw ('Unexpected files in source root: ' + (($unexpected | ForEach-Object Name) -join ', ') +
           '. Extract the official source ZIP into a NEW empty folder to avoid publishing personal files.')
}
$repo = (Read-Host 'GitHub repository name [WorkChatMini]').Trim()
if (-not $repo) { $repo = 'WorkChatMini' }
if ($repo -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,98}$') {
    throw 'Invalid repo name. Use simple letters/numbers/dashes.'
}
Write-Host ''
Write-Host 'WARNING: A PUBLIC GitHub repository exposes this project source to everyone.' -ForegroundColor Yellow
Write-Host 'Review the source for your own secrets/copyrighted private content first.' -ForegroundColor Yellow
$confirm = (Read-Host 'Type PUBLISH PUBLIC to create and push a PUBLIC repository').Trim()
if ($confirm -cne 'PUBLISH PUBLIC') { throw 'Cancelled. Nothing was uploaded.' }
Push-Location -LiteralPath $root
try {
    if (-not (Test-Path -LiteralPath '.git')) {
        & git init -b main
        if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
    }
    $existing = (& git remote 2>$null)
    if ($existing -contains 'origin') { throw 'Remote origin already exists; publish manually to avoid changing a repository unexpectedly.' }
    $name = (& git config user.name)
    $mail = (& git config user.email)
    if (-not $name -or -not $mail) {
        throw 'Set Git author once: git config --global user.name "Your Name" and git config --global user.email "your@email".'
    }
    & git add --all
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    $pending = @(& git diff --cached --name-only)
    $sensitive = @($pending | Where-Object { $_ -match '(?i)(^|/)(\.env(?:\.|$)|[^/]*secret[^/]*|[^/]*credential[^/]*|[^/]*\.key$|[^/]*\.pfx$|[^/]*\.pem$|[^/]*\.exe$|[^/]*\.dll$|[^/]*\.zip$)' -and $_ -cne '_Internal/Source/AI/StoryCredentialStore.cs' })
    if ($sensitive.Count -gt 0) { throw ('Possible sensitive or generated files staged: ' + ($sensitive -join ', ')) }
    if ($pending.Count -eq 0) { throw 'No source files to upload.' }
    Write-Host "Staged $($pending.Count) source files (source only, no user profile)."
    & git commit -m 'WorkChat Mini preview23 release bridge'
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
    & gh repo create $repo --public --source . --remote origin --push
    if ($LASTEXITCODE -ne 0) { throw 'gh repo create / push failed. Check GitHub credentials and whether the repo already exists.' }
    $slug = (& gh repo view --json nameWithOwner --jq '.nameWithOwner').Trim()
    if ($LASTEXITCODE -ne 0 -or -not $slug) { throw 'Could not resolve created repository.' }
    Write-Host "Repository published: https://github.com/$slug" -ForegroundColor Green
    Write-Host ''
    Write-Host 'NEXT: Go to Settings > Pages > Source: GitHub Actions.'
    Write-Host 'THEN: Actions > WorkChat Mini - Build and Publish Windows App > Run workflow.'
    Write-Host 'AFTER SUCCESS: download WorkChatMini-Setup-v2.30.0-preview23.exe from Releases.'
    Write-Host 'The installer will embed the HTTPS latest.json update feed automatically.'
} finally {
    Pop-Location
}
