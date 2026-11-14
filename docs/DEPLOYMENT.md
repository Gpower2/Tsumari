# Local deployment (PowerShell 7)

`Deploy.ps1` is listed under **Solution Items** in Visual Studio. Requires PowerShell 7, the .NET 10 SDK/runtime, and the ignored local runtime settings file created during setup.

```powershell
# Build and validate only; leave the running bot untouched:
pwsh -NoProfile -File ./Deploy.ps1 -ValidateOnly

# Build, validate, stop this release's bot, update it, and start the new version:
pwsh -NoProfile -File ./Deploy.ps1

# Optional explicit deployment/settings locations:
pwsh -NoProfile -File ./Deploy.ps1 -ReleaseDirectory /path/to/tsumari -LocalSecretsPath /path/to/private-settings.json
```

Defaults to `src/Tsumari.Bot/bin/Release/net10.0`. Deployment explicitly uses the **Production** environment. Normal environment-variable configuration overrides remain effective. The launch working directory is always the release directory, so the default relative `tsumari.db` keeps resolving there.

## Windows desktop/taskbar shortcut

Run `pwsh -NoProfile -File ./Install-DeploymentShortcut.ps1` once. It creates **Tsumari Deploy** on the Desktop and in the Start Menu, using the Tsumari icon and a distinct application identity. The shortcut targets the regular installed PowerShell 7 and runs `Deploy.ps1` with `-NoExit`, keeping the result/error window visible. It does not run deployment during installation, request elevation, or change execution-policy settings.

To pin it, open Start, search **Tsumari Deploy**, right-click the app and choose **Pin to taskbar** (or **More → Pin to taskbar**). Pinning is a Windows user-interface action; the installer does not edit taskbar policies/registry or claim that copying a shortcut into a taskbar directory pins it. Clicking the finished shortcut performs a real deployment and restarts the bot if validation succeeds.

The icon is stored in ignored `.local/deployment-shortcut/Tsumari.ico`. Rerun the installer if you move the repository. Optional `-PowerShellPath` selects a portable/custom PowerShell 7, and `-DestinationDirectory` writes a single link to an isolated directory for testing.

## Sequence

1. Acquire a repository deployment lock.
2. Publish Release into a unique `.artifacts/deploy/<id>/publish` directory; intermediate build output is isolated too. Build-time settings injection runs automatically.
3. Run the staged app's `--validate-deployment` command. It checks the merged configuration, credentials being present (never printed), provider/model profiles, relevant options and resolved database path. It never logs in to Discord, calls model endpoints or opens SQLite. A failure identifies the setting/group and prevents any process stop or release copy. It does not prove credentials are accepted remotely or that external services are online.
4. Reject staged database files, unsafe link paths, a changed existing database location, and stale Production configuration that would invalidate preflight.
5. Find only this release's apphost or `dotnet Tsumari.Bot.dll` instance; leave other applications and other Tsumari deployment directories alone. Stop matching processes and wait for exit.
6. Back up only application files that will be overwritten, then copy only manifest application files. There is **no directory mirroring or release-folder deletion**. On copy failure, restore those application files and attempt to restart the previous app.
7. Start the new DLL through `dotnet`, using the release working directory. Windows opens a separate visible PowerShell 7 terminal titled **Tsumari Bot**, via `Run-TsumariConsole.ps1`. Bot stdout/stderr go directly to that terminal: **no log files, output redirects, tee or exit-status files are created**. The deployment script tracks the actual bot PID, not just its console host. The console exits when the bot stops. On Unix the child inherits the current terminal instead of using background `nohup` or file logging. An immediate exit fails deployment with an exit-code error. Surviving the short startup check is not a Discord connectivity/health guarantee.

## SQLite protection

The configured database and its `-wal`, `-shm` and `-journal` sidecars are excluded from copy/rollback, even for an extensionless custom database filename. Files named like `.db`, `.sqlite` or `.sqlite3` and their sidecars/backups are also rejected in published artifacts. Existing database files and unrelated release data are never deleted. If an existing database location changes, deployment stops; database migration is a separate operation.

Process termination can leave SQLite WAL/journal files, so keeping all those files together is essential. The new app may update its database during normal startup/use; the deployment script itself does not copy, replace, checkpoint or migrate the database. Application-file backups are **not database backups**.

The script cleans its uniquely scoped staging directory after success. Failed staging/backup output is retained for diagnosis and contains private settings; it remains Git-ignored. A failure after the new process starts is not an automatic database rollback. Read the terminal output before taking further action. Log files left by earlier deployment versions are not deleted automatically.

If application-file rollback itself fails, the script reports that recovery is incomplete and does not restart potentially mixed application files. The retained backup is available for manual recovery. Startup and preflight also share a retry-option binding fix: configured temperature arrays replace defaults instead of being appended to them.

## Verification

`pwsh -NoProfile -File tests/deployment/Deploy.Tests.ps1` checks copy/rollback, unmanaged data and SQLite/sidecar preservation, database-path protection and staged-file rejection. Add `-WithProcessTests` to build dummy applications in temporary folders and verify exact-release process detection/replacement, paths containing spaces, and survival of unrelated instances. These fixtures never connect to Discord or open a database.

Windows and Linux have explicit process-path inspection; macOS uses `ps` and absolute DLL paths. Ambiguous relative DLL paths fail rather than risk killing an unrelated process. Existing symlink/junction deployment paths are deliberately rejected. Native platform execution should be verified before adopting the script on another OS.
