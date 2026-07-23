[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetDll,
    [Parameter(Mandatory = $true)]
    [string]$StagedDll,
    [string]$BackupDll = "$TargetDll.previous-memory-optimization",
    [string]$ProcessName = 'Lilith'
)

$ErrorActionPreference = 'Stop'
$target = [System.IO.Path]::GetFullPath($TargetDll)
$staged = [System.IO.Path]::GetFullPath($StagedDll)
$backup = [System.IO.Path]::GetFullPath($BackupDll)
$targetDirectory = [System.IO.Path]::GetDirectoryName($target)

if (-not (Test-Path -LiteralPath $staged)) {
    throw "Staged plugin does not exist: $staged"
}
if ([System.IO.Path]::GetDirectoryName($staged) -ne $targetDirectory) {
    throw 'The staged DLL must be in the same plugin directory as the target DLL.'
}

while (Get-Process -Name $ProcessName -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 250
}
Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath $target) {
    Copy-Item -LiteralPath $target -Destination $backup -Force
}
Move-Item -LiteralPath $staged -Destination $target -Force
