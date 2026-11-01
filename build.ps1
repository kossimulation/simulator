# Builds and tests the native core, then the .NET solution.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Step([scriptblock]$Cmd) {
    & $Cmd
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $Cmd" }
}

Invoke-Step { cmake -S native -B build/native }
Invoke-Step { cmake --build build/native --config Release --parallel }
Invoke-Step { ctest --test-dir build/native -C Release --output-on-failure }

Invoke-Step { dotnet build managed/Simulator.sln -c Release }
Invoke-Step { dotnet test managed/Simulator.sln -c Release --no-build }
