<#
.SYNOPSIS
Removes generated build files while preserving source, native inputs and packages.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
try {
    $repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'Avalonia.FFmpegVideoPlayer.csproj') -PathType Leaf)) {
        throw 'Run the cleanup script from an FFmpegVideoPlayer.Avalonia checkout.'
    }
    $projectDirectories = @('', 'FFmpegVideoPlayer.Core', 'FFmpegVideoPlayer.Audio.OpenTK',
        'FFmpegVideoPlayer.Rendering.OpenGL', 'examples/FFmpegVideoPlayerExample', 'tests/FFmpegVideoPlayer.Tests')
    $targets = @('artifacts/bin', 'artifacts/obj', 'artifacts/TestResults')
    foreach ($projectDirectory in $projectDirectories) {
        foreach ($generatedDirectory in @('bin', 'obj', '.tmp-build', 'TestResults')) {
            $targets += if ($projectDirectory) { "$projectDirectory/$generatedDirectory" } else { $generatedDirectory }
        }
    }
    $existingTargets = @()
    foreach ($relativePath in $targets) {
        $target = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
        if (-not $target.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Cleanup target is outside the repository: $target"
        }
        if (-not (Test-Path -LiteralPath $target)) { continue }
        for ($ancestor = $target; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
            if (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Cleanup does not follow directory links: $ancestor"
            }
            if ($ancestor -eq $repositoryRoot) { break }
        }
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($target)
        while ($pending.Count -gt 0) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Cleanup does not follow links: $($item.FullName)"
                }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
                elseif ($item.Extension -in @('.nupkg', '.snupkg', '.db', '.sqlite', '.sqlite3', '.pfx', '.key')) {
                    throw "Unexpected package, data or credential in build output; preserve it before cleaning: $($item.FullName)"
                }
            }
        }
        $existingTargets += $target
    }
    foreach ($target in $existingTargets) {
        if ($PSCmdlet.ShouldProcess($target, 'Remove generated build files')) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.ToString())
    exit 1
}
