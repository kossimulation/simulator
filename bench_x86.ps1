# Builds a 32-bit (x86) bench for the kos_simulation model DLLs (AK13Dynamics.dll,
# DynamicsGorEl.dll are x86): native core for Win32 + self-contained win-x86 publish.
# Result: out\bench-x86\simulator-bench.exe
#
#   ./bench_x86.ps1
#   out\bench-x86\simulator-bench.exe --profile profiles\gorel.json --dll C:\DynamicsNEW\GOR-El\DynamicsGorEl.dll
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Step([scriptblock]$Cmd) {
    & $Cmd
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $Cmd" }
}

Invoke-Step { cmake -S native -B build/native-x86 -A Win32 -DSIMCORE_BUILD_TESTS=OFF }
Invoke-Step { cmake --build build/native-x86 --config Release --parallel }
Invoke-Step { dotnet publish managed/src/Simulator.Bench -c Release -r win-x86 --self-contained `
                -p:SimcoreNativeDir="$PSScriptRoot/build/native-x86/bin/" -o out/bench-x86 }
Write-Host "Bench: out\bench-x86\simulator-bench.exe"
