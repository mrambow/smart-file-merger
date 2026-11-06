param(
    [string]$ExplicitTagName,
    [string]$CommitMessage,
    [string]$OutputFile
)

$ErrorActionPreference = "Stop"

function Main {
    if (IsReleaseSkipped $CommitMessage) {
        Write-Output "Release skipped via commit message directive."
        WriteOutput "skip" "true"
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($ExplicitTagName)) {
        $tag = NormalizeTag $ExplicitTagName
        $version = $tag.TrimStart('v')
        WriteOutputValues $tag $version "false"
        return
    }

    git fetch --tags --force
    $latestTag = GetLatestSemanticTag
    $resolvedTag = CalculateNextVersion $latestTag $CommitMessage
    $resolvedVersion = $resolvedTag.TrimStart('v')

    WriteOutputValues $resolvedTag $resolvedVersion "false"
}

function IsReleaseSkipped([string]$msg) {
    if ([string]::IsNullOrWhiteSpace($msg)) {
        return $false
    }
    return ($msg -match '(?i)\[(skip release|no release)\]')
}

function NormalizeTag([string]$raw) {
    $trimmed = $raw.Trim()
    if ($trimmed.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'v' + $trimmed.Substring(1)
    }
    return "v$trimmed"
}

function GetLatestSemanticTag {
    $tags = git tag -l "v*" --sort=-v:refname
    foreach ($t in $tags) {
        if ($t -match '^v\d+\.\d+\.\d+$') {
            return $t
        }
    }
    return $null
}

function CalculateNextVersion([string]$latestTag, [string]$commitMsg) {
    if ([string]::IsNullOrWhiteSpace($latestTag)) {
        return "v1.0.0"
    }

    if ($latestTag -notmatch '^v(\d+)\.(\d+)\.(\d+)$') {
        throw "Latest tag '$latestTag' does not match semantic versioning format vMajor.Minor.Patch"
    }

    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3]

    if ($commitMsg -match '(?im)^(?:breaking(?: change)?:|[a-z][a-z0-9-]*(?:\([^\r\n()]+\))?!:)|#major') {
        $major++
        $minor = 0
        $patch = 0
    } elseif ($commitMsg -match '(?im)^feat(?:\([^\r\n()]+\))?:|#minor') {
        $minor++
        $patch = 0
    } else {
        $patch++
    }

    return "v$major.$minor.$patch"
}

function WriteOutputValues([string]$tag, [string]$version, [string]$skip) {
    # Validate before writing outputs consumed by the release and build steps.
    $semanticVersionPattern = '\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z'
    if ($tag -cnotmatch $semanticVersionPattern -or $version -cne $tag.Substring(1)) {
        throw "Invalid release tag: expected vMajor.Minor.Patch with optional prerelease and build metadata."
    }

    Write-Output "Resolved release tag: $tag (version: $version)"
    WriteOutput "tag" $tag
    WriteOutput "version" $version
    WriteOutput "skip" $skip
}

function WriteOutput([string]$key, [string]$val) {
    if (-not [string]::IsNullOrWhiteSpace($OutputFile)) {
        "$key=$val" | Out-File -FilePath $OutputFile -Encoding utf8 -Append
    }
}

Main
