#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot 'src/Tsumari.Bot/bin/Release/net10.0'),
    [string]$LocalSecretsPath = (Join-Path $PSScriptRoot '.local/appsettings.secrets.json'),
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-SamePath([string]$Left, [string]$Right) {
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    return [string]::Equals([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)), [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)), $comparison)
}

function Assert-NoDeploymentLink([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Deployment path contains a symbolic link/junction: $current"
            }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Test-DatabaseFile([string]$Path, [string[]]$DatabasePaths) {
    if ([IO.Path]::GetFileName($Path) -match '\.(db|sqlite|sqlite3)(?:$|-(?:wal|shm|journal)$|\.(?:bak|backup|old)(?:$|[.-]))') { return $true }
    foreach ($database in $DatabasePaths) {
        foreach ($suffix in @('', '-wal', '-shm', '-journal')) {
            if (Test-SamePath $Path ($database + $suffix)) { return $true }
        }
    }
    return $false
}

function Get-PreviousDatabasePath([string]$ReleaseRoot) {
    $base = Join-Path $ReleaseRoot 'appsettings.json'
    if (!(Test-Path -LiteralPath $base)) { return $null }
    $path = 'tsumari.db'
    foreach ($file in @($base, (Join-Path $ReleaseRoot 'appsettings.Production.json'))) {
        if (Test-Path -LiteralPath $file) {
            $settings = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
            if ($settings.Contains('Database') -and $settings.Database.Contains('FilePath')) { $path = [string]$settings.Database.FilePath }
        }
    }
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ($entry.Key.Replace('__', ':') -ieq 'Database:FilePath') { $path = [string]$entry.Value }
    }
    return [IO.Path]::GetFullPath($path, $ReleaseRoot)
}

function Get-DeploymentManifest([string]$ArtifactRoot, [string]$ReleaseRoot, [string]$DatabasePath) {
    $previousDatabase = Get-PreviousDatabasePath $ReleaseRoot
    if ($previousDatabase -and (Test-Path -LiteralPath $previousDatabase) -and !(Test-SamePath $previousDatabase $DatabasePath)) {
        throw 'Database:FilePath would change the location of an existing database. Move databases separately; deployment refuses to create a replacement.'
    }
    $protected = @($DatabasePath)
    if ($previousDatabase) { $protected += $previousDatabase }
    Assert-NoDeploymentLink $ReleaseRoot
    $manifest = @()
    foreach ($item in Get-ChildItem -LiteralPath $ArtifactRoot -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Artifact contains a symbolic link: $($item.FullName)" }
        $relative = [IO.Path]::GetRelativePath($ArtifactRoot, $item.FullName)
        $destination = Join-Path $ReleaseRoot $relative
        if ($item.PSIsContainer) {
            if (Test-SamePath $destination $DatabasePath) { throw 'Database:FilePath collides with a published application directory.' }
            continue
        }
        if (Test-DatabaseFile $destination $protected) {
            throw "Artifact contains a database/protected SQLite file: $relative. Deployment will not copy it."
        }
        Assert-NoDeploymentLink $destination
        if (Test-Path -LiteralPath $destination -PathType Container) { throw "An application file would overwrite a directory: $relative" }
        if ((Test-Path -LiteralPath $destination -PathType Leaf) -and (Get-Item -LiteralPath $destination).IsReadOnly) { throw "Release application file is read-only: $relative" }
        $manifest += [pscustomobject]@{ Source = $item.FullName; Destination = $destination; RelativePath = $relative }
    }
    if ($manifest.Count -eq 0) { throw 'Published artifact is empty.' }
    # A stale environment file would change the configuration after staged validation.
    $productionSettings = Join-Path $ReleaseRoot 'appsettings.Production.json'
    if ((Test-Path -LiteralPath $productionSettings) -and !(Test-Path -LiteralPath (Join-Path $ArtifactRoot 'appsettings.Production.json'))) {
        throw 'Release has appsettings.Production.json absent from the artifact. Include it in the build or remove it explicitly before deployment.'
    }
    return $manifest
}

function Get-TsumariDeploymentProcesses([string]$ReleaseRoot) {
    $appHost = Join-Path $ReleaseRoot $(if ($IsWindows) { 'Tsumari.Bot.exe' } else { 'Tsumari.Bot' })
    $dll = Join-Path $ReleaseRoot 'Tsumari.Bot.dll'
    foreach ($process in @(Get-Process -Name 'Tsumari.Bot', 'dotnet' -ErrorAction SilentlyContinue)) {
        try { $executable = $process.Path } catch { throw "Cannot inspect candidate process $($process.Id); deployment has not stopped anything." }
        if ($executable -and (Test-SamePath $executable $appHost)) { $process; continue }
        if ($process.ProcessName -ne 'dotnet') { continue }
        if ($IsWindows) {
            $command = (Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" -ErrorAction Stop).CommandLine
            if ([string]::IsNullOrWhiteSpace($command)) {
                if ($process.HasExited) { continue }
                throw "Cannot inspect the command line of dotnet process $($process.Id)."
            }
            $arguments = $command.Trim()
            if ($arguments.StartsWith('"')) { $arguments = $arguments.Substring($arguments.IndexOf('"', 1) + 1).Trim() }
            else { $arguments = $arguments.Substring($arguments.IndexOf(' ') + 1).Trim() }
            if ($arguments.StartsWith('exec ')) { $arguments = $arguments.Substring(5).Trim() }
            if ($arguments.StartsWith('"')) { $argument = $arguments.Split('"')[1] }
            else { $argument = ($arguments -split '\s+')[0] }
        }
        elseif ($IsLinux) {
            try { $arguments = [IO.File]::ReadAllText("/proc/$($process.Id)/cmdline").Split([char]0) }
            catch { if ($process.HasExited) { continue }; throw "Cannot inspect dotnet process $($process.Id)." }
            if ($arguments.Length -lt 2 -or [string]::IsNullOrWhiteSpace($arguments[1])) { continue }
            if ($arguments[1] -eq 'exec' -and $arguments.Length -lt 3) { continue }
            $argument = if ($arguments[1] -eq 'exec') { $arguments[2] } else { $arguments[1] }
            if (![IO.Path]::IsPathRooted($argument)) {
                $cwd = (Get-Item -LiteralPath "/proc/$($process.Id)/cwd").ResolveLinkTarget($true).FullName
                $argument = [IO.Path]::GetFullPath($argument, $cwd)
            }
        }
        else {
            $command = (& ps -p $process.Id -o command=) -join ''
            if (!$command.Contains('Tsumari.Bot.dll')) { continue }
            $arguments = $command.Trim()
            if ($executable -and $arguments.StartsWith($executable)) { $arguments = $arguments.Substring($executable.Length).Trim() }
            else { $arguments = ($arguments -replace '^\S+\s+', '').Trim() }
            if ($arguments.StartsWith('exec ')) { $arguments = $arguments.Substring(5).Trim() }
            if ($arguments.StartsWith('"' + $dll + '"') -or $arguments -eq $dll -or $arguments.StartsWith($dll + ' ')) { $argument = $dll }
            elseif ($arguments -match '^(/\S+/Tsumari\.Bot\.dll)(?:\s|$)' -and !(Test-SamePath $Matches[1] $dll)) { continue }
            elseif ($arguments -match '^"(/[^"\r\n]+/Tsumari\.Bot\.dll)"' -and !(Test-SamePath $Matches[1] $dll)) { continue }
            else { throw "Cannot safely resolve Tsumari DLL invocation for PID $($process.Id). Launch it with an absolute path." }
        }
        if ([IO.Path]::IsPathRooted($argument) -and (Test-SamePath $argument $dll)) { $process }
        elseif ([IO.Path]::GetFileName($argument) -eq 'Tsumari.Bot.dll' -and ![IO.Path]::IsPathRooted($argument)) {
            throw "Cannot safely identify the release directory of relative-path Tsumari process $($process.Id). Stop it manually first."
        }
        elseif ($IsWindows -and $command.Contains($dll, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Cannot safely identify this release's dotnet invocation (PID $($process.Id)). Stop it manually first."
        }
    }
}

function Start-TsumariDeployment([string]$ReleaseRoot, [switch]$HideConsole) {
    $dll = Join-Path $ReleaseRoot 'Tsumari.Bot.dll'
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    if ($IsWindows) {
        $powerShell = Join-Path $env:ProgramFiles 'PowerShell/7/pwsh.exe'
        if (!(Test-Path -LiteralPath $powerShell)) { $powerShell = Join-Path $PSHOME 'pwsh.exe' }
        $consoleScript = Join-Path $PSScriptRoot 'Run-TsumariConsole.ps1'
        $consoleArguments = '-NoLogo -NoProfile -File "' + $consoleScript + '" -ReleaseDirectory "' + $ReleaseRoot +
            '" -DotnetPath "' + $dotnet + '"'
        $style = if ($HideConsole) { 'Hidden' } else { 'Normal' }
        $console = Start-Process -FilePath $powerShell -ArgumentList $consoleArguments -WorkingDirectory $ReleaseRoot -WindowStyle $style -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            if ($console.HasExited) { break }
            $bots = @(Get-TsumariDeploymentProcesses $ReleaseRoot | Where-Object {
                $details = Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction Stop
                $null -ne $details -and $details.ParentProcessId -eq $console.Id
            })
            if ($bots.Count -eq 1) {
                $bot = $bots[0]
                Start-Sleep -Seconds 3
                $bot.Refresh()
                if (!$bot.HasExited) {
                    $bot | Add-Member -NotePropertyName DeploymentConsoleProcessId -NotePropertyValue $console.Id
                    return $bot
                }
                break
            }
            Start-Sleep -Milliseconds 250
        } while ([DateTime]::UtcNow -lt $deadline)
        $console.Refresh()
        $code = if ($console.HasExited) { $console.ExitCode } else { 'unknown' }
        throw "Tsumari did not stay running in its terminal (exit $code). No log files were created."
    }
    $parameters = @{
        FilePath = $dotnet; ArgumentList = ('"' + $dll + '"'); WorkingDirectory = $ReleaseRoot
        NoNewWindow = $true
        PassThru = $true
    }
    $process = Start-Process @parameters
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw "Tsumari exited during startup (exit $($process.ExitCode)). Output was written to the terminal only." }
    return $process
}

function Install-TsumariArtifact([object[]]$Manifest, [string]$ReleaseRoot, [string]$BackupRoot) {
    # Only manifest application files are touched; there is deliberately no mirror/delete operation.
    $changed = [System.Collections.Generic.List[object]]::new()
    try {
        foreach ($file in $Manifest) {
            Assert-NoDeploymentLink $file.Destination
            $backup = Join-Path $BackupRoot $file.RelativePath
            $existed = Test-Path -LiteralPath $file.Destination -PathType Leaf
            if ($existed) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
                [IO.File]::Copy($file.Destination, $backup, $true)
            }
            $changed.Add([pscustomobject]@{ Destination = $file.Destination; Backup = $backup; Existed = $existed })
            New-Item -ItemType Directory -Path (Split-Path -Parent $file.Destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.Source -Destination $file.Destination -Force
        }
    }
    catch {
        $failure = $_
        $restore = $changed.ToArray()
        [Array]::Reverse($restore)
        $rollbackErrors = [System.Collections.Generic.List[string]]::new()
        foreach ($file in $restore) {
            try {
                Assert-NoDeploymentLink $file.Destination
                if ($file.Existed) { [IO.File]::Copy($file.Backup, $file.Destination, $true) }
                elseif (Test-Path -LiteralPath $file.Destination) { Remove-Item -LiteralPath $file.Destination -Force }
            }
            catch { $rollbackErrors.Add($_.Exception.Message) }
        }
        $message = if ($rollbackErrors.Count -eq 0) { 'Application copy failed; previous application files restored.' } else { 'Application copy failed and rollback was incomplete; do not restart until files are recovered from the retained backup.' }
        $deploymentError = [InvalidOperationException]::new("$message $($failure.Exception.Message) $($rollbackErrors -join '; ')")
        $deploymentError.Data['ApplicationRestored'] = $rollbackErrors.Count -eq 0
        throw $deploymentError
    }
}

function Invoke-TsumariDeployment {
    [CmdletBinding()]
    param([string]$ReleaseDirectory, [string]$LocalSecretsPath, [switch]$ValidateOnly)
    $repository = [IO.Path]::GetFullPath($PSScriptRoot)
    $releaseRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ReleaseDirectory))
    if ($releaseRoot.Contains('"') -or (Test-SamePath $releaseRoot ([IO.Path]::GetPathRoot($releaseRoot))) -or (Test-SamePath $releaseRoot $repository) -or
        (Test-SamePath $releaseRoot (Join-Path $repository 'src/Tsumari.Bot'))) { throw 'ReleaseDirectory must be a dedicated application output directory.' }
    $artifactsRoot = Join-Path $repository '.artifacts/deploy'
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ($releaseRoot.StartsWith($artifactsRoot, $comparison) -or $artifactsRoot.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'ReleaseDirectory must not contain or be inside the temporary deployment artifacts directory.'
    }
    Assert-NoDeploymentLink $releaseRoot
    if (Test-Path -LiteralPath $releaseRoot -PathType Leaf) { throw 'ReleaseDirectory points to a file.' }
    if (!(Test-Path -LiteralPath $LocalSecretsPath -PathType Leaf)) { throw 'LocalSecretsPath is missing. Supply the ignored local runtime settings file.' }
    $lockDirectory = Join-Path $repository '.local'
    New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
    try { $deploymentLock = [IO.File]::Open((Join-Path $lockDirectory 'deployment.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another deployment is in progress (or the deployment lock cannot be opened).' }
    $stage = Join-Path $artifactsRoot ([Guid]::NewGuid().ToString('N'))
    $oldEnvironment = $env:DOTNET_ENVIRONMENT
    $success = $false
    try {
        $env:DOTNET_ENVIRONMENT = 'Production'
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        $publish = Join-Path $stage 'publish'
        Write-Host 'Publishing the current code into an isolated artifact folder...'
        & dotnet publish (Join-Path $repository 'src/Tsumari.Bot/Tsumari.Bot.csproj') --configuration Release --artifacts-path (Join-Path $stage 'build') --output $publish "-p:LocalSecretsPath=$([IO.Path]::GetFullPath($LocalSecretsPath))" --nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE. Running Tsumari has not been stopped." }
        $dll = Join-Path $publish 'Tsumari.Bot.dll'
        $reportPath = Join-Path $stage 'validation.json'
        Write-Host 'Validating the final staged configuration without starting the bot...'
        & dotnet $dll --validate-deployment $releaseRoot $reportPath
        if ($LASTEXITCODE -ne 0) { throw "Artifact validation failed (exit $LASTEXITCODE). See the exact configuration error above. Running Tsumari has not been stopped." }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $manifest = @(Get-DeploymentManifest $publish $releaseRoot $report.DatabaseFilePath)
        # Preflight launcher dependencies and paths before terminating any process.
        Get-Command dotnet -CommandType Application -ErrorAction Stop | Out-Null
        if ($IsWindows -and !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Run-TsumariConsole.ps1') -PathType Leaf)) { throw 'Run-TsumariConsole.ps1 is missing; running bot has not been stopped.' }
        if ($ValidateOnly) { Write-Host 'Preflight passed. ValidateOnly: no process stopped, release files copied, or app started.'; $success = $true; return }

        $running = @(Get-TsumariDeploymentProcesses $releaseRoot)
        foreach ($process in $running) {
            Write-Host "Stopping this release's Tsumari process $($process.Id)..."
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            if (!$process.WaitForExit(15000)) { throw "Tsumari process $($process.Id) did not stop; release files were not copied." }
        }
        try { Install-TsumariArtifact $manifest $releaseRoot (Join-Path $stage 'previous-application') }
        catch {
            if ($running.Count -gt 0 -and $_.Exception.Data['ApplicationRestored'] -eq $true) { Start-TsumariDeployment $releaseRoot | Out-Null }
            throw
        }
        $started = Start-TsumariDeployment $releaseRoot
        Write-Host "Deployed and started Tsumari PID $($started.Id). Database and SQLite sidecar files were excluded from all copy/rollback operations."
        $success = $true
    }
    finally {
        $env:DOTNET_ENVIRONMENT = $oldEnvironment
        $deploymentLock.Dispose()
        if ($success -and (Test-Path -LiteralPath $stage)) {
            $resolvedStage = [IO.Path]::GetFullPath($stage)
            $resolvedRoot = [IO.Path]::GetFullPath($artifactsRoot) + [IO.Path]::DirectorySeparatorChar
            if (!$resolvedStage.StartsWith($resolvedRoot, $comparison) -or [IO.Path]::GetFileName($resolvedStage) -notmatch '^[a-f0-9]{32}$') {
                throw 'Refusing to clean an unexpected artifact path.'
            }
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
        elseif (Test-Path -LiteralPath $stage) { Write-Warning "Deployment failed. Staging/backup retained for diagnosis at $stage (contains private settings)." }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-TsumariDeployment -ReleaseDirectory $ReleaseDirectory -LocalSecretsPath $LocalSecretsPath -ValidateOnly:$ValidateOnly
}
