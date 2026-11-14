#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$DotnetPath
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_ENVIRONMENT = 'Production'
$Host.UI.RawUI.WindowTitle = 'Tsumari Bot'
Set-Location -LiteralPath $ReleaseDirectory
$dll = Join-Path $ReleaseDirectory 'Tsumari.Bot.dll'
# Native stdout/stderr go directly to this terminal: no redirects, tee or output files.
& $DotnetPath $dll
exit $LASTEXITCODE
