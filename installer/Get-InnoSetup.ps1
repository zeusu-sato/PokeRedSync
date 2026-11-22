# Installs a pinned Inno Setup into -Destination and writes the path of ISCC.exe to the pipeline.
# The download is checked against a fixed SHA256 before it is run.
# -InstallerPath uses an already downloaded installer (it is checked the same way).
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$InstallerPath
)
$ErrorActionPreference = 'Stop'
$version = '6.7.4'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe'
$sha256 = 'a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c'

$target = [IO.Path]::GetFullPath($Destination)
$iscc = Join-Path $target 'ISCC.exe'
if (Test-Path -LiteralPath $iscc -PathType Leaf) {
    $uninstaller = Join-Path $target 'unins000.exe'
    $existing = if (Test-Path -LiteralPath $uninstaller -PathType Leaf) { (Get-Item -LiteralPath $uninstaller).VersionInfo.ProductVersion } else { '' }
    if ($existing -and $existing.Trim().StartsWith($version)) { return $iscc }
    throw "ISCC.exe in the destination is not Inno Setup ${version}: $existing"
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('innosetup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $installer = Join-Path $work "innosetup-$version.exe"
    if ($InstallerPath) { Copy-Item -LiteralPath $InstallerPath -Destination $installer }
    else {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing
    }
    $actual = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $sha256) { throw "Inno Setup installer hash mismatch: $actual" }
    $log = Join-Path $work 'install.log'
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', "/DIR=`"$target`"", "/LOG=`"$log`"")
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 40 | ForEach-Object { Write-Host $_ } }
        throw "Inno Setup install failed: $($process.ExitCode)"
    }
    if (-not (Test-Path -LiteralPath $iscc -PathType Leaf)) { throw 'ISCC.exe was not installed' }
    return $iscc
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
