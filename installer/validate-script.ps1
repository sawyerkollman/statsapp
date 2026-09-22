[CmdletBinding()]
param([string]$ScriptPath)

if ([string]::IsNullOrWhiteSpace($ScriptPath)) { $ScriptPath = Join-Path $PSScriptRoot 'Stats.iss' }

$text = Get-Content -LiteralPath $ScriptPath -Raw
$references = [regex]::Matches($text, "ExpandConstant\('\{sys\}([^']+?\.exe)'\)")
if ($references.Count -eq 0) { throw 'No {sys} executable references found.' }

$systemDirectory = [Environment]::SystemDirectory
foreach ($reference in $references) {
    $relativePath = $reference.Groups[1].Value
    if (-not $relativePath.StartsWith('\')) { throw "Malformed {sys} executable reference: $($reference.Value)" }
    $executable = Join-Path $systemDirectory $relativePath.TrimStart('\')
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Windows system executable not found: $executable" }
}

Write-Host "Validated $($references.Count) Windows system executable reference(s)."
