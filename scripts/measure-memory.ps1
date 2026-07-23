[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('vanilla', 'bepinex', 'full')]
    [string]$Mode,

    [int]$GamePid = 0,
    [ValidateRange(10, 86400)]
    [int]$DurationSeconds = 1800,
    [ValidateRange(0, 3600)]
    [int]$WarmupSeconds = 300,
    [ValidateRange(1, 60)]
    [int]$IntervalSeconds = 5,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\memory')
)

$ErrorActionPreference = 'Stop'

if (-not ('LilithProcessMemory' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class LilithProcessMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CountersEx2
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public UIntPtr SharedCommitUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr process,
        ref CountersEx2 counters,
        uint size);

    public static ulong GetPrivateWorkingSet(IntPtr process)
    {
        var counters = new CountersEx2();
        counters.cb = (uint)Marshal.SizeOf<CountersEx2>();
        if (!GetProcessMemoryInfo(process, ref counters, counters.cb))
        {
            throw new Win32Exception();
        }
        return counters.PrivateWorkingSetSize.ToUInt64();
    }
}
'@
}

function Resolve-GameProcess {
    param([int]$RequestedPid)

    if ($RequestedPid -gt 0) {
        return Get-Process -Id $RequestedPid -ErrorAction Stop
    }

    $candidate = Get-Process -Name 'Lilith' -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'Lilith.exe is not running. Start the requested baseline and retry.'
    }
    return $candidate
}

function Get-ProcessTreeIds {
    param([int]$RootPid, [object[]]$CimProcesses)

    $ids = [System.Collections.Generic.HashSet[int]]::new()
    [void]$ids.Add($RootPid)
    do {
        $changed = $false
        foreach ($item in $CimProcesses) {
            if ($ids.Contains([int]$item.ParentProcessId) -and $ids.Add([int]$item.ProcessId)) {
                $changed = $true
            }
        }
    } while ($changed)
    return $ids
}

function Get-ProcessRole {
    param([int]$ProcessId, [int]$RootPid, [string]$Name, [string]$CommandLine)

    if ($ProcessId -eq $RootPid) { return 'game' }
    if ($Name -in @('node', 'node.exe') -and $CommandLine -match 'companion[\\/].*server\.js') { return 'companion' }
    if ($Name -like 'LilithMediaBridge*') { return 'media-bridge' }
    if ($Name -like 'UnityCrashHandler*') { return 'crash-handler' }
    return 'child'
}

$game = Resolve-GameProcess $GamePid
$rootPid = $game.Id
$startedUtc = [DateTime]::UtcNow
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$csvPath = Join-Path $resolvedOutput "$stamp-$Mode.csv"
$summaryPath = Join-Path $resolvedOutput "$stamp-$Mode-summary.json"
$samples = [System.Collections.Generic.List[object]]::new()

Write-Host "Measuring mode=$Mode rootPid=$rootPid for $DurationSeconds seconds (warmup $WarmupSeconds seconds)."

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds) {
    $cim = @(Get-CimInstance Win32_Process)
    $treeIds = Get-ProcessTreeIds -RootPid $rootPid -CimProcesses $cim
    $cimByPid = @{}
    foreach ($item in $cim) { $cimByPid[[int]$item.ProcessId] = $item }

    foreach ($pidValue in $treeIds) {
        $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        $cimItem = $cimByPid[$pidValue]
        $commandLine = if ($null -eq $cimItem) { '' } else { [string]$cimItem.CommandLine }
        $samples.Add([pscustomobject]@{
            TimestampUtc = [DateTime]::UtcNow.ToString('o')
            ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            Mode = $Mode
            Role = Get-ProcessRole -ProcessId $pidValue -RootPid $rootPid -Name $process.Name -CommandLine $commandLine
            ProcessName = $process.Name
            Pid = $pidValue
            WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateWorkingSetMB = [Math]::Round(
                [LilithProcessMemory]::GetPrivateWorkingSet($process.Handle) / 1MB,
                2)
            PrivateMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            Handles = $process.HandleCount
            Threads = $process.Threads.Count
        })
    }

    if (-not (Get-Process -Id $rootPid -ErrorAction SilentlyContinue)) {
        Write-Warning 'The game process exited before sampling completed.'
        break
    }
    Start-Sleep -Seconds $IntervalSeconds
}

$samples | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
$postWarmup = @($samples | Where-Object { $_.ElapsedSeconds -ge $WarmupSeconds })
if ($postWarmup.Count -eq 0) { $postWarmup = @($samples) }

$roleSummaries = foreach ($group in ($postWarmup | Group-Object Role, Pid)) {
    $ordered = @($group.Group | Sort-Object ElapsedSeconds)
    $first = $ordered[0]
    $last = $ordered[-1]
    [pscustomobject]@{
        Role = $first.Role
        ProcessName = $first.ProcessName
        Pid = $first.Pid
        Samples = $ordered.Count
        StartPrivateMB = $first.PrivateMB
        EndPrivateMB = $last.PrivateMB
        PrivateDriftMB = [Math]::Round($last.PrivateMB - $first.PrivateMB, 2)
        PeakPrivateMB = [Math]::Round(($ordered | Measure-Object PrivateMB -Maximum).Maximum, 2)
        StartPrivateWorkingSetMB = $first.PrivateWorkingSetMB
        EndPrivateWorkingSetMB = $last.PrivateWorkingSetMB
        PrivateWorkingSetDriftMB = [Math]::Round(
            $last.PrivateWorkingSetMB - $first.PrivateWorkingSetMB,
            2)
        PeakPrivateWorkingSetMB = [Math]::Round(
            ($ordered | Measure-Object PrivateWorkingSetMB -Maximum).Maximum,
            2)
        HandleDrift = $last.Handles - $first.Handles
        ThreadDrift = $last.Threads - $first.Threads
    }
}

$summary = [pscustomobject]@{
    Mode = $Mode
    RootPid = $rootPid
    StartedUtc = $startedUtc.ToString('o')
    DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
    WarmupSeconds = $WarmupSeconds
    IntervalSeconds = $IntervalSeconds
    Csv = $csvPath
    Roles = @($roleSummaries)
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "Samples: $csvPath"
Write-Host "Summary: $summaryPath"
$roleSummaries | Format-Table -AutoSize
