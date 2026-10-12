param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsPath
)

$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem -LiteralPath $ResultsPath -Filter '*.sarif' -File)
if ($files.Count -eq 0) {
    throw 'CodeQL produced no SARIF files.'
}

$findingCount = 0
foreach ($file in $files) {
    $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($report.version -ne '2.1.0' -or -not $report.runs) {
        throw "Invalid CodeQL SARIF report: $($file.Name)"
    }
    foreach ($run in $report.runs) {
        foreach ($invocation in $run.invocations) {
            if ($invocation.executionSuccessful -eq $false) {
                throw "CodeQL analysis failed: $($file.Name)"
            }
        }
        foreach ($result in $run.results) {
            # Informational pass/metadata entries are not findings.
            if ($result.kind -and $result.kind -ne 'fail') { continue }
            if (@($result.suppressions | Where-Object { $_.status -eq 'accepted' }).Count -gt 0) { continue }
            $findingCount++
            # Keep arbitrary source text out of workflow commands in the log.
        }
    }
}

if ($findingCount -gt 0) {
    throw "CodeQL found $findingCount unsuppressed finding(s). See Code Scanning and the codeql-results artifact."
}
Write-Output "CodeQL gate passed: $($files.Count) SARIF report(s), no unsuppressed findings."
