#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$PowerShellPath,
    # Optional isolated destination for verification. Default installs Desktop and Start Menu links.
    [string]$DestinationDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Windows deployment shortcuts require Windows. Deploy.ps1 itself remains portable.' }

$repository = [IO.Path]::GetFullPath($PSScriptRoot)
$deploymentScript = Join-Path $repository 'Deploy.ps1'
$sourceIcon = Join-Path $repository 'src/Tsumari.Bot/tsumari_logo2_final.ico'
foreach ($path in @($deploymentScript, $sourceIcon)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required shortcut resource missing: $path" }
}
if (!$PowerShellPath) {
    $installed = Join-Path $env:ProgramFiles 'PowerShell/7/pwsh.exe'
    if (Test-Path -LiteralPath $installed) { $PowerShellPath = $installed }
    else {
        $PowerShellPath = Get-Command pwsh.exe -CommandType Application -All |
            Where-Object { $_.Source -notmatch 'WindowsApps|\.codex' } |
            Select-Object -First 1 -ExpandProperty Source
    }
}
if (!$PowerShellPath -or !(Test-Path -LiteralPath $PowerShellPath -PathType Leaf)) {
    throw 'A normal PowerShell 7 installation was not found. Supply -PowerShellPath pointing to pwsh.exe.'
}
$PowerShellPath = [IO.Path]::GetFullPath($PowerShellPath)
$major = & $PowerShellPath -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.Major'
if ($LASTEXITCODE -ne 0 -or [int]$major -lt 7) { throw 'The shortcut requires PowerShell 7 or later.' }

if (!('Tsumari.DeploymentShortcutMetadata' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
namespace Tsumari {
    public static class DeploymentShortcutMetadata {
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey {
            public Guid Format; public uint Id;
        }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Value;
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }
        public static void SetAppId(string path, string appId) {
            var shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true));
            var value = new PropVariant { Type = 31, Value = Marshal.StringToCoTaskMemUni(appId) };
            try {
                var persist = (IPersistFile)shellLink;
                persist.Load(path, 2);
                var store = (IPropertyStore)shellLink;
                var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
                Marshal.ThrowExceptionForHR(store.Commit());
                persist.Save(path, true);
            }
            finally {
                Marshal.FreeCoTaskMem(value.Value);
                if (shellLink != null) Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }
}
'@
}

$iconDirectory = Join-Path $repository '.local/deployment-shortcut'
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null
$icon = Join-Path $iconDirectory 'Tsumari.ico'
Copy-Item -LiteralPath $sourceIcon -Destination $icon -Force
$arguments = '-NoLogo -NoProfile -NoExit -File "' + $deploymentScript + '"'
$destinations = if ($DestinationDirectory) {
    @([IO.Path]::GetFullPath($DestinationDirectory))
} else {
    @([Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('Programs'))
}
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($directory in $destinations) {
        if ([string]::IsNullOrWhiteSpace($directory)) { throw 'Windows did not provide a shortcut destination directory.' }
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $shortcutPath = Join-Path $directory 'Tsumari Deploy.lnk'
        $shortcut = $shell.CreateShortcut($shortcutPath)
        try {
            if ((Test-Path -LiteralPath $shortcutPath) -and !$shortcut.Arguments.Contains($deploymentScript, [StringComparison]::OrdinalIgnoreCase)) {
                throw "A different shortcut already exists: $shortcutPath. It was not overwritten."
            }
            $shortcut.TargetPath = $PowerShellPath
            $shortcut.Arguments = $arguments
            $shortcut.WorkingDirectory = $repository
            $shortcut.Description = 'Build, validate, deploy and restart local Tsumari. The window stays open to show results.'
            $shortcut.IconLocation = $icon + ',0'
            $shortcut.WindowStyle = 1
            $shortcut.Save()
        }
        finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null }
        [Tsumari.DeploymentShortcutMetadata]::SetAppId($shortcutPath, 'Tsumari.Deployment')
        Write-Output "Created: $shortcutPath"
    }
}
finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
Write-Output 'No deployment was run. In Start, search Tsumari Deploy, right-click it and choose Pin to taskbar.'
