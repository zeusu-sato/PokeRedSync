# Local check of PokeRedSync-Setup-*.exe in test mode (/POKEREDSYNCTEST).
# Installs into a scratch folder, checks the layout, the test Run value and the shortcut,
# uninstalls, and checks that data is kept. The real Run value, Start menu, uninstall entry,
# Core\Path and %LOCALAPPDATA%\PokeRedSync are only read, before and after, to show they did not change.
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [Parameter(Mandatory = $true)][string]$PackageDir,
    [Parameter(Mandatory = $true)][string]$Scratch
)
$ErrorActionPreference = 'Stop'
$failures = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$label) {
    if ($condition) { Write-Host "OK   $label" } else { Write-Host "FAIL $label" -ForegroundColor Red; $failures.Add($label) }
}
function Wait-Until([scriptblock]$condition, [int]$seconds = 120) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) { if (& $condition) { return $true }; Start-Sleep -Milliseconds 500 }
    return [bool](& $condition)
}
function Get-RealState {
    $run = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
    $core = Get-ItemProperty -LiteralPath 'HKCU:\Software\U.S. Robotics\Pilot Desktop\Core' -ErrorAction SilentlyContinue
    $core32 = $null
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
        $key = $base.OpenSubKey('Software\U.S. Robotics\Pilot Desktop\Core')
        if ($key) { $core32 = $key.GetValue('Path'); $key.Dispose() }
        $base.Dispose()
    } catch { }
    $data = Join-Path $env:LOCALAPPDATA 'PokeRedSync'
    $files = if (Test-Path -LiteralPath $data) { @(Get-ChildItem -LiteralPath $data -Recurse -File -Force | Where-Object { $_.FullName -notlike (Join-Path $data 'Logs\*') } | ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }) } else { @() }
    [pscustomobject]@{
        Run       = if ($run) { "$($run.PokeRedSync)" } else { '' }
        CorePath  = if ($core) { "$($core.Path)" } else { '' }
        CorePath32 = "$core32"
        Uninstall = Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{BA8043DB-5310-40A6-B259-B146AC7DD6F5}_is1'
        Shortcut  = Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) 'PokeRedSync.lnk')
        DataFiles = ($files | Sort-Object) -join "`n"
    }
}

$scratchPath = [IO.Path]::GetFullPath($Scratch)
if (Test-Path -LiteralPath $scratchPath) { throw "Scratch folder already exists: $scratchPath" }
New-Item -ItemType Directory -Path $scratchPath | Out-Null
$app = Join-Path $scratchPath 'app'
$dataDir = Join-Path $scratchPath 'data'
New-Item -ItemType Directory -Path $dataDir | Out-Null
[IO.File]::WriteAllText((Join-Path $dataDir 'keep.txt'), 'keep')
$testKey = 'HKCU:\Software\PokeRedSync-InstallerTest'
if (Test-Path -LiteralPath $testKey) { throw "Test key already exists: $testKey" }

$before = Get-RealState
try {
    New-Item -Path $testKey | Out-Null
    New-ItemProperty -LiteralPath $testKey -Name 'UpdateFailedTag' -Value 'v2026.1122c' | Out-Null
    $process = Start-Process -FilePath ([IO.Path]::GetFullPath($Setup)) -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$app`"", "/POKEREDSYNCTEST=`"$scratchPath`"", "/LOG=`"$(Join-Path $scratchPath 'install.log')`"") -Wait -PassThru
    Check ($process.ExitCode -eq 0) "install exit code $($process.ExitCode)"

    Check (-not (Get-ItemProperty -LiteralPath $testKey -Name 'UpdateFailedTag' -ErrorAction SilentlyContinue)) 'test UpdateFailedTag removed'
    [string[]]$names = @(Get-ChildItem -LiteralPath $app -File | ForEach-Object { $_.Name })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    Check (($names -join ',') -ceq 'LICENSE,PokeRedSync.exe,installer-test.txt,unins000.dat,unins000.exe') "installed files: $($names -join ',')"
    Check (-not (Get-ChildItem -LiteralPath $app -Directory)) 'no sub folder in app'
    foreach ($name in 'LICENSE', 'PokeRedSync.exe') {
        $expected = (Get-FileHash -LiteralPath (Join-Path $PackageDir $name) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash
        Check ($expected -eq $actual) "hash of $name"
    }
    $run = (Get-ItemProperty -LiteralPath "$testKey\Run" -ErrorAction SilentlyContinue).PokeRedSync
    Check ($run -eq "`"$app\PokeRedSync.exe`"") "test Run value: $run"
    Check (Test-Path -LiteralPath (Join-Path $scratchPath 'StartMenu\PokeRedSync.lnk')) 'shortcut in test Start menu'

    # The updater keeps the old files here; uninstall must remove it.
    New-Item -ItemType Directory -Path (Join-Path $app 'previous') | Out-Null
    Copy-Item -LiteralPath (Join-Path $app 'LICENSE') -Destination (Join-Path $app 'previous\LICENSE')

    $uninstaller = Join-Path $app 'unins000.exe'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $scratchPath 'uninstall.log')`"") -Wait -PassThru
    Check ($process.ExitCode -eq 0) "uninstall exit code $($process.ExitCode)"
    $gone = Wait-Until { -not (Test-Path -LiteralPath $uninstaller) -and -not (Test-Path -LiteralPath (Join-Path $app 'PokeRedSync.exe')) }
    Check $gone 'program files removed'
    Check (-not (Test-Path -LiteralPath (Join-Path $app 'previous'))) 'previous removed'
    Check (-not (Test-Path -LiteralPath $app) -or -not (Get-ChildItem -LiteralPath $app -Force)) 'app folder empty or removed'
    Check (-not (Test-Path -LiteralPath (Join-Path $scratchPath 'StartMenu\PokeRedSync.lnk'))) 'shortcut removed'
    $run = (Get-ItemProperty -LiteralPath "$testKey\Run" -ErrorAction SilentlyContinue).PokeRedSync
    Check (-not $run) 'test Run value removed'
    Check ((Test-Path -LiteralPath (Join-Path $dataDir 'keep.txt')) -and ([IO.File]::ReadAllText((Join-Path $dataDir 'keep.txt')) -ceq 'keep')) 'scratch data kept'
}
finally {
    if (Test-Path -LiteralPath $testKey) { Remove-Item -LiteralPath $testKey -Recurse -Force }
}
$after = Get-RealState
foreach ($property in 'Run', 'CorePath', 'CorePath32', 'Uninstall', 'Shortcut', 'DataFiles') {
    Check ($before.$property -ceq $after.$property) "real $property unchanged"
}
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed'
