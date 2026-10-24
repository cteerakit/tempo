# Runs Tempo under `dotnet watch` so code and XAML edits show up without a manual rebuild and relaunch.
# Usage: pwsh tools/dev.ps1   (Ctrl+C to stop; the Windows taskbar is restored afterwards)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Tempo.App' | Resolve-Path

function Stop-Tempo {
    # A running copy owns the single-instance mutex, so a dev build would just open its settings and exit.
    Get-Process -Name Tempo -ErrorAction SilentlyContinue | Stop-Process -Force
}

Stop-Tempo

# Edits Hot Reload can't apply restart the app automatically instead of prompting.
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = '1'

try {
    dotnet watch --project $project
}
finally {
    # Killing the process skips Tempo's own shutdown, so put the taskbar back explicitly.
    Stop-Tempo
    dotnet run --project $project --no-build -- --restore-taskbar
}
