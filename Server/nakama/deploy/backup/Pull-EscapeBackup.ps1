#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-zA-Z0-9_][a-zA-Z0-9_.-]*@[a-zA-Z0-9][a-zA-Z0-9.:-]*$')]
    [string]$SshTarget,

    [Parameter(Mandatory = $true)]
    [string]$IdentityFile,

    [Parameter(Mandatory = $true)]
    [string]$KnownHostsFile,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [ValidateRange(1, 65535)]
    [int]$Port = 22,

    [ValidateRange(1, 10000)]
    [int]$KeepLatest = 48,

    [ValidateRange(1, 168)]
    [int]$MaxAgeHours = 26,

    [ValidateRange(30, 3600)]
    [int]$TransferTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$archivePattern = '^escape-([0-9]{8}T[0-9]{6}Z)\.dump\.age$'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$utcStyle = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal

function Join-ProcessArguments {
    param([string[]]$Arguments)

    # ProcessStartInfo.ArgumentList is unavailable on Windows PowerShell 5.1.
    $quoted = foreach ($argument in $Arguments) {
        '"' + ($argument -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
    }
    return $quoted -join ' '
}

function Invoke-BackupSsh {
    param([string]$RemoteCommand, [string]$OutputPath)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $sshExecutable
    $startInfo.Arguments = Join-ProcessArguments ($sshArguments + @($RemoteCommand))
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $output = [IO.File]::Open($OutputPath, 'CreateNew', 'Write', 'None')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Unable to start OpenSSH.'
        }

        # Copy bytes directly. PowerShell text redirection corrupts binary stdout in 5.1.
        $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $readError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TransferTimeoutSeconds * 1000)) {
            try {
                $process.Kill()
            } catch [InvalidOperationException] {
                if (-not $process.HasExited) { throw }
            }
            $null = $process.WaitForExit(5000)
            throw "OpenSSH timed out after $TransferTimeoutSeconds seconds."
        }
        $null = $copy.GetAwaiter().GetResult()
        $sshError = $readError.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0) {
            throw "OpenSSH exited with code $($process.ExitCode). $sshError"
        }
        $output.Flush($true)
    } finally {
        $process.Dispose()
        $output.Dispose()
    }
}

function Get-BackupManifest {
    $manifestPath = Join-Path $backupDirectory ([Guid]::NewGuid().ToString('N') + '.partial')
    try {
        Invoke-BackupSsh 'list' $manifestPath
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($manifest.schema -ne 1 -or $null -eq $manifest.backups) {
            throw 'Invalid or empty Escape backup manifest.'
        }

        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $backups = foreach ($entry in $manifest.backups) {
            $name = [string]$entry.name
            if ($name -cnotmatch $archivePattern) {
                throw 'Invalid archive name in Escape backup manifest.'
            }

            $createdUtc = [DateTimeOffset]::MinValue
            $bytes = 0L
            if (-not [DateTimeOffset]::TryParseExact($Matches[1], "yyyyMMdd'T'HHmmss'Z'", $invariantCulture, $utcStyle, [ref]$createdUtc) -or
                -not [long]::TryParse([string]$entry.bytes, [Globalization.NumberStyles]::None, $invariantCulture, [ref]$bytes) -or
                $bytes -lt 1 -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$' -or -not $seen.Add($name)) {
                throw "Invalid metadata for $name in Escape backup manifest."
            }

            [pscustomobject]@{ Name = $name; CreatedUtc = $createdUtc; Bytes = $bytes; Sha256 = [string]$entry.sha256 }
        }
        if (-not $backups) {
            throw 'The Escape server has no completed backups.'
        }

        $ordered = @($backups | Sort-Object Name -Descending)
        $latest = $ordered[0]
        $now = [DateTimeOffset]::UtcNow
        if ($latest.CreatedUtc -gt $now.AddMinutes(5) -or $latest.CreatedUtc -lt $now.AddHours(-$MaxAgeHours)) {
            throw "Latest Escape backup $($latest.Name) is outside the expected UTC window ($MaxAgeHours hours old at most)."
        }
        return $ordered
    } finally {
        if ([IO.File]::Exists($manifestPath)) { [IO.File]::Delete($manifestPath) }
    }
}

function Test-BackupFile {
    param([string]$Path, [psobject]$Backup)

    $file = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    return ($file -is [IO.FileInfo] -and $file.Length -eq $Backup.Bytes -and
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Backup.Sha256)
}

function Move-BackupFile {
    param([string]$TemporaryPath, [string]$TargetPath)

    if ([IO.File]::Exists($TargetPath)) {
        [IO.File]::Replace($TemporaryPath, $TargetPath, [NullString]::Value)
    } else {
        [IO.File]::Move($TemporaryPath, $TargetPath)
    }
}

function Write-BackupChecksum {
    param([psobject]$Backup, [string]$ArchivePath)

    $target = "$ArchivePath.sha256"
    $content = "$($Backup.Sha256.ToLowerInvariant())  $($Backup.Name)`n"
    $current = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    if ($current -is [IO.FileInfo] -and $current.Length -eq $content.Length -and
        [IO.File]::ReadAllText($target, [Text.Encoding]::ASCII) -ceq $content) {
        return
    }

    $temporary = Join-Path $backupDirectory ([Guid]::NewGuid().ToString('N') + '.partial')
    try {
        [IO.File]::WriteAllText($temporary, $content, [Text.Encoding]::ASCII)
        Move-BackupFile $temporary $target
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Receive-BackupFile {
    param([psobject]$Backup)

    $target = Join-Path $backupDirectory $Backup.Name
    if (-not (Test-BackupFile $target $Backup)) {
        $temporary = Join-Path $backupDirectory ([Guid]::NewGuid().ToString('N') + '.partial')
        try {
            Invoke-BackupSsh "get $($Backup.Name)" $temporary
            if (-not (Test-BackupFile $temporary $Backup)) {
                throw "Escape backup $($Backup.Name) failed the size or SHA-256 check."
            }

            Move-BackupFile $temporary $target
        } finally {
            if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        }
    }
    Write-BackupChecksum $Backup $target
}

$sshExecutable = (Get-Command ssh.exe -CommandType Application | Select-Object -First 1).Source
$key = Get-Item -LiteralPath $IdentityFile -Force
$knownHosts = Get-Item -LiteralPath $KnownHostsFile -Force
if ($key -isnot [IO.FileInfo] -or $knownHosts -isnot [IO.FileInfo] -or $knownHosts.Length -eq 0) {
    throw 'Provide an SSH private key file and a nonempty pinned known_hosts file.'
}

$pinnedHosts = $knownHosts.FullName.Replace('\', '/')
if ($pinnedHosts -match '%|\$\{') {
    throw 'The known_hosts path cannot contain OpenSSH expansion tokens.'
}

$sshArguments = @(
    '-F', 'none', '-T', '-p', [string]$Port, '-i', $key.FullName,
    '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
    '-o', 'PreferredAuthentications=publickey', '-o', 'StrictHostKeyChecking=yes',
    '-o', "UserKnownHostsFile=`"$pinnedHosts`"", '-o', 'GlobalKnownHostsFile=none',
    '-o', 'UpdateHostKeys=no', '-o', 'ClearAllForwardings=yes', '-o', 'ConnectTimeout=15',
    '-o', 'ServerAliveInterval=15', '-o', 'ServerAliveCountMax=2', '-o', 'LogLevel=ERROR',
    '--', $SshTarget
)

$backupDirectory = [IO.Directory]::CreateDirectory($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)).FullName
$lock = [IO.File]::Open((Join-Path $backupDirectory '.pull.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $backups = @(Get-BackupManifest | Select-Object -First $KeepLatest)
    try {
        foreach ($backup in $backups) { Receive-BackupFile $backup }
    } catch {
        # The server can rotate its older archive between listing and download.
        Write-Warning "Escape backup pull failed once. Reloading the manifest. $($_.Exception.Message)"
        $backups = @(Get-BackupManifest | Select-Object -First $KeepLatest)
        foreach ($backup in $backups) { Receive-BackupFile $backup }
    }

    Get-ChildItem -LiteralPath $backupDirectory -File -Force |
        Where-Object Name -CMatch $archivePattern |
        Sort-Object Name -Descending |
        Select-Object -Skip $KeepLatest |
        ForEach-Object {
            $archive = $_.FullName
            Remove-Item -LiteralPath $archive -Force
            $checksum = "$archive.sha256"
            if ([IO.File]::Exists($checksum)) { [IO.File]::Delete($checksum) }
        }

    Write-Output "Escape backup verified: $($backups[0].Name) SHA-256 $($backups[0].Sha256). Checked $($backups.Count) archive(s)."
} finally {
    $lock.Dispose()
}
