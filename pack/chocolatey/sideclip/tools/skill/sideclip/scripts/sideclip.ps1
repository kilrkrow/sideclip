<#
.SYNOPSIS
  List recent Windows screenshots or call Sideclip's picker.

.PARAMETER Command
  shots (default), newest, picker, pick, help

.PARAMETER N
  How many shots to list. Default 3.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'shots',

    [Parameter(Position = 1)]
    [int]$N = 3
)

$ErrorActionPreference = 'Stop'

function Get-ScreenshotFolder {
    Join-Path ([Environment]::GetFolderPath('MyPictures')) 'Screenshots'
}

function Get-NewestShots {
    param([int]$Count = 3)
    if ($Count -lt 1) { $Count = 1 }
    $folder = Get-ScreenshotFolder
    if (-not (Test-Path -LiteralPath $folder)) {
        return @()
    }
    Get-ChildItem -LiteralPath $folder -File -Force |
        Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::Hidden) } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First $Count
}

function Write-ShotsJson {
    param($Files)
    $rows = @(
        foreach ($f in $Files) {
            [pscustomobject]@{
                path      = $f.FullName
                name      = $f.Name
                lastWrite = $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
            }
        }
    )
    $json = $rows | ConvertTo-Json -Compress
    if ($rows.Count -eq 1 -and $json -notmatch '^\s*\[') {
        return "[$json]"
    }
    if ($rows.Count -eq 0) {
        return '[]'
    }
    return $json
}

function Find-SideclipExe {
    $cmd = Get-Command sideclip -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }

    $candidates = @(
        (Join-Path $PSScriptRoot '..\..\..\..\bin\Debug\net8.0-windows\Sideclip.exe'),
        (Join-Path $PSScriptRoot '..\..\..\..\bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe'),
        (Join-Path $PSScriptRoot '..\..\..\..\artifacts\publish\win-x64\Sideclip.exe')
    )
    foreach ($c in $candidates) {
        $resolved = [IO.Path]::GetFullPath($c)
        if (Test-Path -LiteralPath $resolved) { return $resolved }
    }
    return $null
}

$Command = $Command.TrimStart('-', '/')
if ($N -lt 1) { $N = 3 }

switch ($Command.ToLowerInvariant()) {
    { $_ -in @('help', 'h', '?') } {
        @'
sideclip.ps1 shots [n]   List n newest files in Pictures\Screenshots as JSON (default 3)
sideclip.ps1 newest      Print the newest screenshot path
sideclip.ps1 picker      Open the Sideclip clipboard picker
sideclip.ps1 pick        Open the picker and print the chosen clip as JSON
'@
        return
    }
    { $_ -in @('shots', 'list') } {
        $files = @(Get-NewestShots -Count $N)
        if ($files.Count -eq 0) {
            Write-Output '[]'
            $folder = Get-ScreenshotFolder
            if (-not (Test-Path -LiteralPath $folder)) {
                [Console]::Error.WriteLine("Screenshots folder is missing: $folder")
            }
            return
        }
        Write-Output (Write-ShotsJson -Files $files)
        return
    }
    { $_ -in @('newest', 'latest') } {
        $files = @(Get-NewestShots -Count 1)
        if ($files.Count -eq 0) {
            [Console]::Error.WriteLine('No screenshots yet.')
            exit 1
        }
        Write-Output $files[0].FullName
        return
    }
    { $_ -in @('picker', 'pick') } {
        $exe = Find-SideclipExe
        if (-not $exe) {
            [Console]::Error.WriteLine('Sideclip.exe not found. Start Sideclip or put it on PATH.')
            exit 2
        }
        $arg = if ($Command -eq 'pick') { '--pick' } else { '--picker' }
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.Arguments = $arg
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $p = New-Object System.Diagnostics.Process
        $p.StartInfo = $psi
        [void]$p.Start()
        $stdout = $p.StandardOutput.ReadToEnd()
        $stderr = $p.StandardError.ReadToEnd()
        $p.WaitForExit()
        if ($stdout) { [Console]::Out.Write($stdout) }
        if ($stderr) { [Console]::Error.Write($stderr) }
        exit $p.ExitCode
    }
    default {
        [Console]::Error.WriteLine("Unknown command: $Command")
        exit 2
    }
}
