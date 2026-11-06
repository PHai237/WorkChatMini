#requires -Version 5.1
<#
  Generates the ECDSA P-256 key pair that signs latest.json (see docs/UPDATE-SIGNING.md).
  Nothing is written to disk: copy the two printed values into GitHub, then close the window.
#>
$ErrorActionPreference = 'Stop'
$ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::NamedCurves.nistP256)
try {
    $p = $ecdsa.ExportParameters($true)
    # Fixed SubjectPublicKeyInfo prefix for an uncompressed P-256 point (works on Windows PowerShell 5.1 too).
    $prefix = [byte[]](0x30,0x59,0x30,0x13,0x06,0x07,0x2A,0x86,0x48,0xCE,0x3D,0x02,0x01,0x06,0x08,0x2A,0x86,0x48,0xCE,0x3D,0x03,0x01,0x07,0x03,0x42,0x00,0x04)
    $publicKey = [Convert]::ToBase64String([byte[]]($prefix + $p.Q.X + $p.Q.Y))
    $privateJson = (@{ d = [Convert]::ToBase64String($p.D); x = [Convert]::ToBase64String($p.Q.X); y = [Convert]::ToBase64String($p.Q.Y) } | ConvertTo-Json -Compress)
    $privateKey = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($privateJson))
}
finally { $ecdsa.Dispose() }

Write-Host ''
Write-Host '1) GitHub > Settings > Secrets and variables > Actions > Variables > New repository variable' -ForegroundColor Cyan
Write-Host '   Name : UPDATE_SIGNING_PUBLIC_KEY'
Write-Host "   Value: $publicKey"
Write-Host ''
Write-Host '2) Same page > Secrets > New repository secret' -ForegroundColor Cyan
Write-Host '   Name : UPDATE_SIGNING_PRIVATE_KEY'
Write-Host "   Value: $privateKey"
Write-Host ''
Write-Host 'Keep the private value secret and back it up: losing it means existing installs can never accept another update.' -ForegroundColor Yellow
Write-Host 'Ship the first release built WITH the public key as a manual install; older builds ignore the signature.' -ForegroundColor Yellow
