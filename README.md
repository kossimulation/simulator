# Simulator

Симулятор динаміки польоту **вертольота** та **літака** з шістьма ступенями
свободи.

| Шар | Технологія | Папка |
|---|---|---|
| Фізичне ядро | C++17, CMake → `libsimcore` (C ABI) | `native/` |
| Застосунок, сценарії, телеметрія | C# / .NET 8, P/Invoke | `managed/` |
| Розробка моделей, генерація коду | MATLAB / Simulink, Embedded Coder | `matlab/` |

Схему шарів, системи координат і порядок підключення моделей Simulink описано
в [docs/architecture.md](docs/architecture.md).

## Що вже працює

- 6DOF рівняння руху твердого тіла (кватерніони, RK4, підкроки ≤ 5 мс).
- Атмосфера ISA, вітер.
- Легкий літак класу Cessna 172: лінійні аеродинамічні похідні, тяга.
- Легкий вертоліт класу Bo-105: несучий гвинт за імпульсною теорією,
  циклічний і загальний крок, хвостовий гвинт.
- Трим на висіння та горизонтальний політ (метод Ньютона).
- Спрощений контакт із землею.
- C# обгортка `Simulation` і консольний застосунок, що пише телеметрію в CSV.
- MATLAB: параметри моделей і скрипти генерації C++ з Simulink.
- Тести: 13 нативних і 8 на C#. CI на Linux і Windows.

## Вимоги

- CMake ≥ 3.20 і компілятор C++17 (MSVC 2019+, GCC 9+, Clang 10+)
- .NET SDK 8.0
- MATLAB/Simulink + Simulink Coder + Embedded Coder (лише для генерації коду)

## Збірка та запуск

Linux / macOS:

```bash
./build.sh            # нативне ядро + тести, потім .NET + тести
```

Windows (PowerShell):

```powershell
./build.ps1
```

Вручну:

```bash
cmake -S native -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native --config Release
ctest --test-dir build/native -C Release --output-on-failure

dotnet test managed/Simulator.sln -c Release
```

Приклади польотів:

```bash
# Вертоліт: висіння на 100 м, на 5-й секунді загальний крок +0.05
dotnet run --project managed/src/Simulator.Cli -c Release -- \
    --vehicle helicopter --speed 0 --altitude 100 --duration 20 --throttle-step 0.05 --out heli.csv

# Літак: 50 м/с на 1000 м, на 5-й секунді ручка на себе +0.1
dotnet run --project managed/src/Simulator.Cli -c Release -- \
    --vehicle fixed-wing --speed 50 --altitude 1000 --duration 30 --pitch-step 0.1 --out fw.csv
```

## MATLAB / Simulink

```matlab
cd matlab
startup_simulator            % шляхи + параметри (fw, heli, sim_dt)
generate_code('my_model')    % C++ -> native/generated/my_model
```

Потім зберіть ядро з `-DSIMCORE_WITH_GENERATED=ON`. Контракт портів моделі
описано в [matlab/models/README.md](matlab/models/README.md).

## Структура

```
native/
  include/simcore/simcore.h   C ABI (межа з C#)
  src/                        фізика: rigid_body, atmosphere, fixed_wing, helicopter, simulation
  tests/                      нативні тести
  generated/                  код з Embedded Coder
managed/
  src/Simulator.Core/         P/Invoke + клас Simulation
  src/Simulator.Cli/          консольний раннер, CSV
  tests/Simulator.Core.Tests/ xUnit
matlab/
  params/                     параметри моделей (синхронно з C++)
  codegen/                    configure_codegen.m, generate_code.m
  models/                     .slx моделі
docs/architecture.md
```

## Можливі наступні кроки

- 3D візуалізація (C#: Unity, Godot або WPF + HelixToolkit) та керування джойстиком.
- Перша модель у Simulink (наприклад, несучий гвинт або САК), яка пройде
  через генерацію коду.
- Звалювання літака, динаміка індуктивного потоку й вплив землі для вертольота.
- Єдине джерело параметрів (JSON/YAML), яке читатимуть і C++, і MATLAB.
