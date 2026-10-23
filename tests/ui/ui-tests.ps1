# UI tests for the Tempo settings window, driven through UI Automation with `winapp ui`.
# Usage: start Tempo, open Settings (run Tempo.exe again), then: .\tests\ui\ui-tests.ps1 -AppPid <pid>
# The user's settings.json is backed up first and restored at the end.
param([Parameter(Mandatory)][int]$AppPid, [string]$OutDir)
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'out' }

$ErrorActionPreference = 'Continue'
$pass = 0; $fail = 0; $results = @()
$settingsPath = Join-Path $env:APPDATA 'Tempo\settings.json'
$backup = "$settingsPath.uitest-backup"
Copy-Item $settingsPath $backup -Force
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
$hwnd = ($windows | Where-Object { $_.title -eq 'Tempo settings' } | Select-Object -First 1).hwnd
if (-not $hwnd) { throw 'Settings window not found; open it first.' }

function Settings { Get-Content $settingsPath -Raw | ConvertFrom-Json }

function Wait-Settings([scriptblock]$Condition, [int]$TimeoutMs = 3000) {
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    do {
        try { if (& $Condition (Settings)) { return } } catch { }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)
    throw 'settings.json never reached the expected state'
}

function Test-UI([string]$Name, [scriptblock]$Script) {
    try {
        $global:LASTEXITCODE = 0
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) { $script:pass++; $script:results += @{ name = $Name; status = 'PASS' } }
        else { $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$output" } }
    } catch {
        $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

function Ui { winapp ui @args -w $hwnd -q }

# Selects a list item through UI Automation by its text, skipping same-named labels.
function Click-ListItem([string]$Text, [int]$Nth = 0) {
    $found = @((winapp ui search $Text -w $hwnd --json 2>$null | ConvertFrom-Json).matches |
        Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Text })
    if ($found.Count -le $Nth) { throw "List item '$Text' not found" }
    Ui invoke $found[$Nth].selector | Out-Null
}

function Select-Combo([string]$Combo, [string]$Item) {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([intptr]$hwnd)
    $control = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Combo))
    if (-not $control) { throw "Combo '$Combo' not found" }
    $expand = $control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    try {
        Start-Sleep -Milliseconds 200
        $items = $control.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Item))
        foreach ($candidate in $items) {
            $pattern = $null
            if ($candidate.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
                $pattern.Select()
                return
            }
        }
        throw "Selectable item '$Item' not found in '$Combo'"
    } finally { $expand.Collapse() }
}
try {
    # --- Layout ---
    Test-UI 'Layout tab' { Ui invoke TabLayout; Ui wait-for LibraryList -t 3000 }
    Test-UI 'Zones present' {
        foreach ($zone in 'ZoneLeft', 'ZoneCenter', 'ZoneRight') {
            if ((winapp ui search $zone -w $hwnd --json 2>$null | ConvertFrom-Json).matchCount -lt 1) { throw "$zone missing" }
        }
    }
    $before = @((Settings).layout.right).Count
    Test-UI 'Add a Clock to the right zone' {
        Click-ListItem 'Clock' 0
        Ui invoke BtnAddRight | Out-Null
        Wait-Settings { param($s) @($s.layout.right).Count -eq $before + 1 -and $s.layout.right[-1].typeId -eq 'tempo.clock' }
    }
    Test-UI 'New widget is selected and its editor shows' { Ui wait-for 'Show date' -t 2000 }
    Test-UI 'Editor writes widget settings' {
        Ui invoke 'Show date' | Out-Null
        Wait-Settings { param($s) $s.layout.right[-1].settings.showDate -eq $false }
    }
    Test-UI 'Move it earlier' {
        Ui invoke BtnEarlier | Out-Null
        Wait-Settings { param($s) $s.layout.right[-2].typeId -eq 'tempo.clock' -and $s.layout.right[-2].settings.showDate -eq $false }
    }
    Test-UI 'Remove it' {
        Ui invoke BtnRemove | Out-Null
        Wait-Settings { param($s) @($s.layout.right).Count -eq $before }
    }
    winapp ui screenshot -w $hwnd -o "$OutDir\01-layout.png" -q 2>$null | Out-Null

    # --- Appearance ---
    Test-UI 'Appearance tab' { Ui invoke TabAppearance; Ui wait-for ThemeList -t 3000 }
    Test-UI 'Pick the Midnight theme' {
        Ui invoke 'Midnight' | Out-Null
        Wait-Settings { param($s) $s.appearance.themeId -eq 'Midnight' }
    }
    Test-UI 'Pick a violet accent' {
        Ui invoke AccentViolet | Out-Null
        Wait-Settings { param($s) $s.appearance.accentColor -eq '#A78BFA' }
    }
    Test-UI 'Blur: Blur' {
        Select-Combo CmbBackdrop 'Blur'
        Wait-Settings { param($s) $s.appearance.backdrop -eq 'Blur' }
    }
    winapp ui screenshot -w $hwnd -o "$OutDir\02-appearance.png" -q 2>$null | Out-Null
    Test-UI 'Reset appearance keeps theme' {
        Ui invoke BtnResetAppearance | Out-Null
        Wait-Settings { param($s) $s.appearance.themeId -eq 'Midnight' -and -not $s.appearance.accentColor -and -not $s.appearance.backdrop }
    }

    # --- Behavior ---
    Test-UI 'Behavior tab' { Ui invoke TabBehavior; Ui wait-for CmbMode -t 3000 }
    Test-UI 'Switch to floating pill' {
        Select-Combo CmbMode 'Floating pill'
        Wait-Settings { param($s) $s.bar.mode -eq 'Pill' }
    }
    Test-UI 'Pill options enabled' { Ui wait-for ChkReserve -p IsEnabled --value True -t 2000 }
    Start-Sleep -Milliseconds 800
    $barHwnd = ($windows | Where-Object { $_.title -eq 'Tempo' } | Select-Object -First 1).hwnd
    winapp ui screenshot -w $barHwnd -o "$OutDir\03-bar-pill.png" -q 2>$null | Out-Null
    Test-UI 'Move the bar to the top' {
        Select-Combo CmbEdge 'Top'
        Wait-Settings { param($s) $s.bar.edge -eq 'Top' }
    }
    Start-Sleep -Milliseconds 800
    winapp ui screenshot -w $hwnd -o "$OutDir\04-behavior.png" -q 2>$null | Out-Null
    Test-UI 'Back to bottom, full width' {
        Select-Combo CmbEdge 'Bottom'
        Select-Combo CmbMode 'Full width'
        Wait-Settings { param($s) $s.bar.edge -eq 'Bottom' -and $s.bar.mode -eq 'Full' }
    }
    Test-UI 'Toggle full-screen hiding' {
        Ui invoke ChkFullscreen | Out-Null
        Wait-Settings { param($s) $s.behavior.hideOnFullscreen -eq $false }
        Ui invoke ChkFullscreen | Out-Null
        Wait-Settings { param($s) $s.behavior.hideOnFullscreen -eq $true }
    }

    # --- Accessibility: every interactive control has an AutomationId or a name ---
    $elements = (winapp ui inspect -w $hwnd --interactive --json 2>$null | ConvertFrom-Json).elements
    $unnamed = @($elements | Where-Object {
        $_.type -match 'Button|ComboBox|CheckBox|Slider|Edit|TabItem' -and -not $_.automationId -and -not $_.name
    })
    if ($unnamed.Count -eq 0) { $pass++; $results += @{ name = 'Controls are identifiable'; status = 'PASS' } }
    else { $fail++; $results += @{ name = 'Controls are identifiable'; status = 'FAIL'; detail = ($unnamed | ForEach-Object type) -join ', ' } }
}
finally {
    Copy-Item $backup $settingsPath -Force
    Remove-Item $backup
}

Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object { Write-Host "  FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red }
$results | ConvertTo-Json | Out-File "$OutDir\test-results.json"
if ($fail -gt 0) { exit 1 } else { exit 0 }


