# Sample existing Windows processes; never launches or stops a bot.
param(
    [string[]]$ProcessNames = @('RSBot', 'RSBot.Manager'),
    [ValidateRange(1, 86400)][int]$Seconds = 900,
    [ValidateRange(1, 60)][int]$IntervalSeconds = 2,
    [string]$Scenario = 'idle',
    [string]$Output = 'resources.csv'
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Output) { throw "Output already exists: $Output" }
$processors = [Environment]::ProcessorCount
$clock = [Diagnostics.Stopwatch]::StartNew()
$previous = @{}
while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
    $current = @{}
    foreach ($process in @(Get-Process -Name $ProcessNames -ErrorAction SilentlyContinue)) {
        try {
            $process.Refresh()
            $key = '{0}:{1}' -f $process.Id, $process.StartTime.ToUniversalTime().Ticks
            $now = $clock.Elapsed.TotalSeconds
            $cpu = $process.TotalProcessorTime.TotalSeconds
            $usage = $null
            if ($previous.ContainsKey($key)) {
                $elapsed = $now - $previous[$key].Time
                if ($elapsed -gt 0) {
                    $usage = [Math]::Round(100 * ($cpu - $previous[$key].Cpu) / $elapsed / $processors, 3)
                }
            }
            $current[$key] = @{ Time = $now; Cpu = $cpu }
            [pscustomobject]@{
                Utc = [DateTime]::UtcNow.ToString('o')
                Scenario = $Scenario
                Process = $process.ProcessName
                ProcessId = $process.Id
                LogicalProcessors = $processors
                CpuSeconds = $cpu
                CpuMachinePercent = $usage
                PrivateBytes = $process.PrivateMemorySize64
                WorkingSetBytes = $process.WorkingSet64
                Threads = $process.Threads.Count
                Handles = $process.HandleCount
                Responding = $process.Responding
            } | Export-Csv -LiteralPath $Output -NoTypeInformation -Append
        }
        catch [System.InvalidOperationException] {
            # A process exited during the snapshot; continue with the surviving processes.
        }
        finally { $process.Dispose() }
    }
    $previous = $current
    Start-Sleep -Seconds $IntervalSeconds
}
