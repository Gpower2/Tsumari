#requires -Version 7.0
param([switch]$WithProcessTests)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repository 'Deploy.ps1')
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Assert-Fails([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null } catch { Assert ($_.Exception.Message.Contains($Expected)) "Unexpected error: $($_.Exception.Message)"; return }
    throw "Expected failure: $Expected"
}
$testParent = Join-Path $repository '.artifacts/deployment-tests'
$testRoot = Join-Path $testParent ([Guid]::NewGuid().ToString('N'))
$artifact = Join-Path $testRoot 'artifact'
$release = Join-Path $testRoot 'release'
$backup = Join-Path $testRoot 'backup'
$ownedProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
try {
    New-Item -ItemType Directory -Path $artifact, $release -Force | Out-Null
    Set-Content (Join-Path $release 'appsettings.json') '{"Database":{"FilePath":"custom-store"}}'
    Set-Content (Join-Path $artifact 'appsettings.json') '{"Database":{"FilePath":"custom-store"}}'
    Set-Content (Join-Path $release 'version.txt') 'old'
    Set-Content (Join-Path $artifact 'version.txt') 'new'
    Set-Content (Join-Path $artifact 'Microsoft.Data.Sqlite.dll') 'application library, not a database'
    Set-Content (Join-Path $release 'unmanaged.txt') 'retain me'
    $database = Join-Path $release 'custom-store'
    $hashes = @{}
    foreach ($suffix in @('', '-wal', '-shm', '-journal')) {
        Set-Content ($database + $suffix) "database bytes $suffix"
        $hashes[$suffix] = (Get-FileHash ($database + $suffix)).Hash
    }
    $manifest = @(Get-DeploymentManifest $artifact $release $database)
    Install-TsumariArtifact $manifest $release $backup
    Assert ((Get-Content (Join-Path $release 'version.txt')) -eq 'new') 'Application update failed.'
    Assert (Test-Path (Join-Path $release 'unmanaged.txt')) 'Unmanaged data was deleted.'
    foreach ($suffix in $hashes.Keys) { Assert ((Get-FileHash ($database + $suffix)).Hash -eq $hashes[$suffix]) "SQLite file modified: $suffix" }
    Set-Content (Join-Path $artifact 'bad.sqlite-wal') 'forbidden'
    Assert-Fails { Get-DeploymentManifest $artifact $release $database } 'database/protected SQLite'
    Remove-Item -LiteralPath (Join-Path $artifact 'bad.sqlite-wal')
    Set-Content (Join-Path $artifact 'custom-store') 'forbidden'
    Assert-Fails { Get-DeploymentManifest $artifact $release $database } 'database/protected SQLite'
    Remove-Item -LiteralPath (Join-Path $artifact 'custom-store')
    Assert-Fails { Get-DeploymentManifest $artifact $release (Join-Path $release 'different.db') } 'change the location'
    Set-Content (Join-Path $release 'appsettings.Production.json') '{}'
    Assert-Fails { Get-DeploymentManifest $artifact $release $database } 'absent from the artifact'
    Remove-Item -LiteralPath (Join-Path $release 'appsettings.Production.json')
    Set-Content (Join-Path $release 'version.txt') 'before rollback'
    $script:copyCount = 0
    function Copy-Item {
        param([string]$LiteralPath, [string]$Destination, [switch]$Force)
        $script:copyCount++
        if ($script:copyCount -eq 2) { throw 'Injected application copy failure' }
        [IO.File]::Copy($LiteralPath, $Destination, $true)
    }
    Assert-Fails { Install-TsumariArtifact $manifest $release (Join-Path $testRoot 'rollback') } 'previous application files restored'
    Remove-Item Function:Copy-Item
    Assert ((Get-Content (Join-Path $release 'version.txt')) -eq 'before rollback') 'Rollback failed.'
    foreach ($suffix in $hashes.Keys) { Assert ((Get-FileHash ($database + $suffix)).Hash -eq $hashes[$suffix]) "SQLite modified by rollback: $suffix" }
    Assert (Test-SamePath ($release + [IO.Path]::DirectorySeparatorChar) $release) 'Trailing separator path comparison failed.'
    if ($IsWindows) {
        function Start-Process {
            param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle, [switch]$PassThru)
            Assert ($WindowStyle -eq 'Normal') 'The default bot console would be hidden.'
            Assert ($ArgumentList.Contains('Run-TsumariConsole.ps1')) 'The bot console wrapper was not selected.'
            Assert (!$ArgumentList.Contains('-LogPath') -and !$ArgumentList.Contains('-ExitStatusPath')) 'The launcher still requests output files.'
            throw 'Default visible-console launch verified'
        }
        Assert-Fails { Start-TsumariDeployment $release } 'Default visible-console launch verified'
        Remove-Item Function:Start-Process
    }
    Write-Host 'PASS: application copy, data/SQLite-sidecar preservation, protected artifact rejection, database relocation rejection, stale settings rejection, rollback, canonical paths, visible Windows launch default.'

    if ($WithProcessTests) {
        $fixture = Join-Path $testRoot 'fixture'
        New-Item -ItemType Directory -Path $fixture -Force | Out-Null
        Set-Content (Join-Path $fixture 'Fixture.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>Tsumari.Bot</AssemblyName></PropertyGroup></Project>'
        Set-Content (Join-Path $fixture 'Program.cs') 'System.Console.WriteLine("Fixture console output"); System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);'
        $first = Join-Path $testRoot 'instance one'
        $second = Join-Path $testRoot 'instance two'
        & dotnet publish (Join-Path $fixture 'Fixture.csproj') --output $first --nologo --verbosity quiet
        if ($LASTEXITCODE) { throw 'Fixture publish failed.' }
        New-Item -ItemType Directory -Path $second -Force | Out-Null
        Get-ChildItem -LiteralPath $first -File | Copy-Item -Destination $second
        $parameters = @{ PassThru = $true }
        if ($IsWindows) { $parameters.WindowStyle = 'Hidden' }
        $appHostName = if ($IsWindows) { 'Tsumari.Bot.exe' } else { 'Tsumari.Bot' }
        $one = Start-Process -FilePath (Join-Path $first $appHostName) @parameters
        $two = Start-Process -FilePath (Join-Path $second $appHostName) @parameters
        $three = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"' + (Join-Path $first 'Tsumari.Bot.dll') + '"') @parameters
        $ownedProcesses.Add($one); $ownedProcesses.Add($two); $ownedProcesses.Add($three)
        Start-Sleep -Seconds 2
        $found = @(Get-TsumariDeploymentProcesses $first)
        Assert ($found.Count -eq 2 -and $one.Id -in $found.Id -and $three.Id -in $found.Id) 'Process discovery selected the wrong instance.'
        foreach ($process in $found) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(10000) | Out-Null }
        $two.Refresh()
        Assert (!$two.HasExited) 'Unrelated deployment was stopped.'
        $started = Start-TsumariDeployment $first -HideConsole
        $ownedProcesses.Add($started)
        if ($IsWindows) { $ownedProcesses.Add((Get-Process -Id $started.DeploymentConsoleProcessId)) }
        Assert (!$started.HasExited) 'New fixture failed to start.'
        Assert (!(Test-Path -LiteralPath (Join-Path $first 'logs'))) 'The console launcher created a log directory.'
        Assert (@(Get-ChildItem -LiteralPath $first -Recurse -File | Where-Object {$_.Name -match '\.log$|\.exit-code$|^nohup\.out$'}).Count -eq 0) 'The console launcher created output files.'
        Write-Host 'PASS: exact-release apphost/dotnet process discovery, scoped termination, unrelated instance survives, replacement runs with terminal-only output and no log files.'
    }
}
finally {
    foreach ($process in $ownedProcesses) { if (!$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(10000) | Out-Null } }
    if ($testRoot.StartsWith([IO.Path]::GetFullPath($testParent) + [IO.Path]::DirectorySeparatorChar) -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
