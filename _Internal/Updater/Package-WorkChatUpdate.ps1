param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'Built EXE not found.' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-preview\d+(?:\.\d+)?)?$') { throw 'Invalid release version.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('WorkChatPackage-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $exe = Join-Path $work 'WorkChatMini.exe'
    Copy-Item -LiteralPath $Executable -Destination $exe
    $manifest = [ordered]@{
        version = $Version
        file = 'WorkChatMini.exe'
        sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        size = (Get-Item -LiteralPath $exe).Length
    }
    [IO.File]::WriteAllText((Join-Path $work 'update.json'), ($manifest | ConvertTo-Json -Compress), ([System.Text.UTF8Encoding]::new($false)))
    if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }
    Compress-Archive -LiteralPath (Join-Path $work 'WorkChatMini.exe'),(Join-Path $work 'update.json') -DestinationPath $Output -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $Output -PathType Leaf)) { throw 'Package output was not created.' }
    Write-Host ('Binary update package: ' + $Output)
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
