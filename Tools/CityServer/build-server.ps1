<#
.SYNOPSIS
  Builds the Linux city server in Unity, rebuilds the Docker image, starts it, and checks both ports.
.PARAMETER SkipUnityBuild
  Reuse the existing Builds/CityServer output and only rebuild the image and restart the container.
.NOTES
  Close any Unity Editor that has this project open first: batch-mode builds cannot share a project.
#>
param([switch]$SkipUnityBuild)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serverDll = Join-Path $project 'Builds\CityServer\HTownSimServer_Data\Managed\HTownSim.World.dll'

if (-not $SkipUnityBuild) {
    $started = Get-Date
    $logFile = Join-Path $env:TEMP 'htownsim-server-build.log'
    Write-Host 'Building Linux city server in Unity...'
    # The CLI exits 0 even when the player build fails, so judge by the log and by fresh output.
    # Unity writes licensing notices to stderr; in Windows PowerShell 5.1 that would stop the script, so it is not treated as an error here.
    $ErrorActionPreference = 'Continue'
    & unity build --target StandaloneLinux64 --execute-method HTownSim.EditorTools.BuildGame.BuildCityServer $project *> $logFile
    $ErrorActionPreference = 'Stop'
    if (Select-String -Path $logFile -Pattern 'Error building Player|Build Failed' -Quiet) {
        Select-String -Path $logFile -Pattern 'Error building Player|error CS' | Select-Object -First 5 | ForEach-Object { Write-Host $_.Line }
        throw "Unity build failed. Full log: $logFile"
    }
    # A change to editor code or a player setting leaves the runtime DLLs alone, so look at the player settings file as well.
    $settings = Join-Path $project 'Builds\CityServer\HTownSimServer_Data\globalgamemanagers'
    $fresh = (Test-Path $serverDll) -and (Test-Path $settings) -and ((Get-Item $serverDll).LastWriteTime -ge $started -or (Get-Item $settings).LastWriteTime -ge $started)
    if (-not $fresh) {
        throw "Unity did not produce a fresh server build. Full log: $logFile"
    }
}

Write-Host 'Building image and starting container...'
Push-Location $PSScriptRoot
try {
    docker compose up -d --build --wait --wait-timeout 180
    if ($LASTEXITCODE -ne 0) {
        docker compose logs --tail 40
        throw 'Container did not become healthy.'
    }
}
finally {
    Pop-Location
}

foreach ($port in 7777, 7778) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', $port)
        $stream = $client.GetStream()
        $stream.ReadTimeout = 4000
        $buffer = New-Object byte[] 64
        $count = $stream.Read($buffer, 0, 64)
        Write-Host ("port {0}: {1}" -f $port, [Text.Encoding]::ASCII.GetString($buffer, 0, $count).Trim())
    }
    finally {
        $client.Dispose()
    }
}

Write-Host 'City server is up.'
