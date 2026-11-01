#!/usr/bin/env bash
# Builds and tests the native core, then the .NET solution.
set -euo pipefail
cd "$(dirname "$0")"

cmake -S native -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native --config Release --parallel
ctest --test-dir build/native -C Release --output-on-failure

dotnet build managed/Simulator.sln -c Release
dotnet test managed/Simulator.sln -c Release --no-build
