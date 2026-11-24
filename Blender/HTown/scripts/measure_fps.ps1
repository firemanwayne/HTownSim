# Runs HTownSim's MeasureFrameRate probe once in Play Mode and prints the log.
#   powershell -File scripts/measure_fps.ps1
$p = 'J:\Unity\projects\HTownSim'
unity command editor.playmode start --project-path $p --result-only 2>$null | Out-Null
Start-Sleep 12
(unity command script run --file AgentScripts/MeasureFrameRate.cs --entry MeasureFrameRate.Start --project-path $p --result-only 2>$null | ConvertFrom-Json).result | Out-Null
Start-Sleep 58
for ($i = 0; $i -lt 12; $i++) {
    $r = (unity command script run --file AgentScripts/MeasureFrameRate.cs --entry MeasureFrameRate.Report --project-path $p --result-only 2>$null | ConvertFrom-Json).result
    if ($r -match 'DONE') { break }
    Start-Sleep 5
}
$r
unity command editor.playmode stop --project-path $p --result-only 2>$null | Out-Null
Start-Sleep 3
