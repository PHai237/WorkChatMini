# Signed update feed (optional, 2.60.7)

`latest.json` carries a SHA-256 of the update ZIP. That protects against a corrupted download but not against
someone who controls the feed host (they could publish both a malicious ZIP and its hash). Signing closes that gap.

## How it works
- Signature: ECDSA P-256 / SHA-256 (IEEE P1363) over `version|packageUrl|sha256-lowercase`, stored as `signature` in `latest.json`.
- The public key (base64 SubjectPublicKeyInfo) is compiled into the EXE as assembly metadata `WorkChatUpdatePublicKey`.
- An EXE built **with** a key rejects any feed whose signature is missing or invalid ("Update feed signature is missing or invalid").
  An EXE built **without** a key behaves exactly like 2.60.6 and ignores the field.
- The release workflow refuses to publish an unsigned feed when `UPDATE_SIGNING_PUBLIC_KEY` is set but the private key secret is missing.

## Enable it
1. Run `_Internal/Tools/New-UpdateSigningKey.ps1` (Windows PowerShell or pwsh; prints two values, writes nothing).
2. Add repository **variable** `UPDATE_SIGNING_PUBLIC_KEY` and repository **secret** `UPDATE_SIGNING_PRIVATE_KEY`.
3. Release as usual. Users on older builds update normally; the first build that embeds the key enforces signatures from then on.

Back up the private key. If it is lost, installed copies can no longer verify a new feed and must be reinstalled manually.
