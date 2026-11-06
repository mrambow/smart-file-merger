$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../scripts/resolve-release-version.ps1" -CommitMessage '[skip release]'

function AssertEqual($actual, $expected) {
    if ($actual -cne $expected) {
        throw "Expected '$expected', got '$actual'"
    }
}

$versionCases = @(
    @('fix: repair', 'v1.2.4'),
    @('defeat: incidental text', 'v1.2.4'),
    @('PR body mentions feat: casually', 'v1.2.4'),
    @('mentions breaking change: casually', 'v1.2.4'),
    @('unbreaking: incidental text', 'v1.2.4'),
    @('feat: add feature', 'v1.3.0'),
    @('feat(ui): add feature', 'v1.3.0'),
    @("Merge PR`n`nfeat(ui): add feature", 'v1.3.0'),
    @('breaking: change API', 'v2.0.0'),
    @('breaking change: change API', 'v2.0.0'),
    @('BREAKING CHANGE: change API', 'v2.0.0'),
    @('feat!: change API', 'v2.0.0'),
    @('fix(api)!: change API', 'v2.0.0'),
    @("Merge PR`r`n`r`nfix(api)!: change API", 'v2.0.0'),
    @('mentions fix!: casually', 'v1.2.4'),
    @('release #major', 'v2.0.0'),
    @('release #minor', 'v1.3.0'),
    @("feat: feature`nrelease #major", 'v2.0.0')
)
foreach ($case in $versionCases) {
    AssertEqual (CalculateNextVersion 'v1.2.3' $case[0]) $case[1]
}
AssertEqual (CalculateNextVersion '' 'feat!: first release') 'v1.0.0'
AssertEqual (IsReleaseSkipped 'fix: repair [no release]') $true
AssertEqual (IsReleaseSkipped 'fix: repair [skip release]') $true

$outputPath = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
$script:OutputFile = $outputPath
try {
    foreach ($raw in @('1.2.3', 'v0.0.0', 'V1.2.3', 'v1.2.3-rc.1', 'v1.2.3+build.001', 'v1.2.3-alpha.0+sha.abc-123')) {
        Remove-Item $outputPath -ErrorAction SilentlyContinue
        $tag = NormalizeTag $raw
        $version = $tag.Substring(1)
        WriteOutputValues $tag $version 'false'
        AssertEqual ((Get-Content $outputPath) -join '|') "tag=$tag|version=$version|skip=false"
    }
    foreach ($invalid in @('', 'v1.2', 'v01.2.3', 'v1.2.3-01', 'v1.2.3-', 'v1.2.3+', 'v1.2.3-alpha..1', 'v1.2.3+build..1', 'v1.2.3;whoami', 'v1.2.3$(whoami)', 'v1.2.3"', "v1.2.3`n", "v1.2.3`nversion=9.9.9")) {
        Remove-Item $outputPath -ErrorAction SilentlyContinue
        $rejected = $false
        try { WriteOutputValues $invalid $invalid.TrimStart('v') 'false' } catch { $rejected = $true }
        AssertEqual $rejected $true
        AssertEqual (Test-Path $outputPath) $false
    }
    $rejected = $false
    try { WriteOutputValues 'v1.2.3' '1.2.3;whoami' 'false' } catch { $rejected = $true }
    AssertEqual $rejected $true
} finally {
    Remove-Item $outputPath -ErrorAction SilentlyContinue
}
# A failed native command can still emit plausible tags. Never consume them.
$script:CommitMessage = 'fix: repair'
$script:ExplicitTagName = ''
try {
    function git {
        if ($args[0] -eq 'fetch') {
            $global:LASTEXITCODE = 23
            return
        }
        throw 'Tag listing must not run after a failed fetch.'
    }
    $failure = ''
    try { Main } catch { $failure = $_.Exception.Message }
    AssertEqual $failure 'Failed to fetch release tags (git exit code 23).'

    function git {
        $global:LASTEXITCODE = 24
        'v9.9.9'
    }
    $failure = ''
    try { GetLatestSemanticTag } catch { $failure = $_.Exception.Message }
    AssertEqual $failure 'Failed to list release tags (git exit code 24).'
} finally {
    Remove-Item Function:git
}
Write-Output 'Release version regression checks passed.'
