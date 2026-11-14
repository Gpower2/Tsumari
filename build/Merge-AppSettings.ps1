param(
    [Parameter(Mandatory)][string]$BasePath,
    [Parameter(Mandatory)][string]$ModelsPath,
    [Parameter(Mandatory)][string]$SecretsPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
function Merge-Settings([System.Collections.IDictionary]$destination, [System.Collections.IDictionary]$overlay) {
    foreach ($key in $overlay.Keys) {
        if ($destination[$key] -is [System.Collections.IDictionary] -and $overlay[$key] -is [System.Collections.IDictionary]) {
            Merge-Settings $destination[$key] $overlay[$key]
        } else { $destination[$key] = $overlay[$key] }
    }
}
$settings = [ordered]@{}
foreach ($path in @($BasePath, $ModelsPath, $SecretsPath)) {
    if (Test-Path -LiteralPath $path) {
        $overlay = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        Merge-Settings $settings $overlay
    }
}
$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
