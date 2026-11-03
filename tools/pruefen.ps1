# Blocklist check: no local paths, real names, company names or e-mail addresses
# in versioned files or commit messages.
#
#   powershell -File tools/pruefen.ps1                  checks all files in the git index
#   powershell -File tools/pruefen.ps1 -MessageFile f   checks a commit message file
#
# Used by .githooks/pre-commit and .githooks/commit-msg
# (activate once with: git config core.hooksPath .githooks).
# This file itself is excluded from the check because it has to name the patterns.
# Keep it ASCII only: Windows PowerShell 5.1 reads .ps1 files as ANSI.

param([string]$MessageFile)

$ErrorActionPreference = 'Stop'

# Patterns as extended regular expressions (valid for git grep -E and .NET).
$patterns = @(
    'Anwender',
    'OneDrive',
    '\bIBJ\b',
    'Ingenieurgesellschaft',
    '[A-Za-z]:\\+Users\\+',
    '/Users/[A-Za-z]'
)
$mailPattern = '[A-Za-z0-9._%+-]+@([A-Za-z0-9-]+\.)+[A-Za-z]{2,}'
$allowedMailDomains = @('users.noreply.github.com', 'anthropic.com')
$selfPath = 'tools/pruefen.ps1'

function Get-Git {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe'
    if (Test-Path $fallback) { return $fallback }
    throw 'git not found'
}

function Test-MailAllowed([string]$text) {
    foreach ($m in [regex]::Matches($text, $mailPattern)) {
        $domain = $m.Value.Substring($m.Value.IndexOf('@') + 1).ToLowerInvariant()
        $ok = $false
        foreach ($d in $allowedMailDomains) {
            if ($domain -eq $d -or $domain.EndsWith('.' + $d)) { $ok = $true }
        }
        if (-not $ok) { return $false }
    }
    return $true
}

$found = New-Object System.Collections.Generic.List[string]

if ($MessageFile) {
    $lines = Get-Content -LiteralPath $MessageFile -Encoding UTF8 | Where-Object { -not $_.StartsWith('#') }
    $n = 0
    foreach ($line in $lines) {
        $n++
        foreach ($p in $patterns) {
            if ([regex]::IsMatch($line, $p, 'IgnoreCase')) { $found.Add("Commit-Nachricht:${n}: [$p] $line") }
        }
        if (-not (Test-MailAllowed $line)) { $found.Add("Commit-Nachricht:${n}: [E-Mail] $line") }
    }
}
else {
    $git = Get-Git
    $exclude = ":(exclude)$selfPath"

    # File contents in the index (what will be committed); binary files are skipped (-I).
    foreach ($p in $patterns) {
        $out = & $git grep --cached -I -n -i -E -e $p -- . $exclude 2>$null
        foreach ($l in $out) { $found.Add("[$p] $l") }
    }
    $out = & $git grep --cached -I -n -E -e $mailPattern -- . $exclude 2>$null
    foreach ($l in $out) {
        if (-not (Test-MailAllowed $l)) { $found.Add("[E-Mail] $l") }
    }

    # File names in the index.
    $names = & $git ls-files --cached
    foreach ($name in $names) {
        foreach ($p in $patterns) {
            if ([regex]::IsMatch($name, $p, 'IgnoreCase')) { $found.Add("[$p] Dateiname: $name") }
        }
    }
}

if ($found.Count -gt 0) {
    Write-Host 'Sperrliste: folgende Stellen duerfen nicht ins Repo:' -ForegroundColor Red
    foreach ($f in $found) { Write-Host "  $f" }
    Write-Host 'Bitte entfernen bzw. neutral formulieren und erneut committen.' -ForegroundColor Red
    exit 1
}

Write-Host 'Sperrliste: ok'
exit 0
