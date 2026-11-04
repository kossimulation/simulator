# Моделі kos_simulation (AK13Dynamics, DynamicsGorEl)

Готові моделі динаміки збираються з Simulink кастомним таргетом
`kos_simulation` (`dll_malloc.tlc`) у DLL з однаковим ABI. Цей ABI розуміють
SimCore і Transas. Проєкт підключає такі DLL без змін і без перекомпіляції
через `Simulator.Core.Kos.KosModel` (C#).

| Модель | Апарат | Порти | Крок |
|---|---|---|---|
| `AK13Dynamics.dll` | вертоліт AK1-3 | 31 вхід, 61 + 42 виходи (`slx\ak13_ports.m`) | `DYN_STEP_S` (0 → `DT_DYN` = 0.00625 с) |
| `DynamicsGorEl.dll` | БПЛА GOR-Electric | 40 входів, 55 виходів | кадр 0.025 с, підкроки `DYN_STEP_S` / `SIM_STEP_S` |

## ABI

Вісім експортів (`<model>_mdlinterface.def`), усі `cdecl`:

```c
void*  mdlCreate(void);                 // NULL = помилка ініціалізації
void   mdlStart(void* m);
void   mdlStep(void* m);                // один базовий крок моделі
void   mdlDestroy(void* m);
double getTimeStepSize(void* m);
double getCurrentTime(void* m);
int    descrCounter(void);              // кількість дескрипторів портів
struct description descrByIndex(void* m, int index);

struct description {                    // trs_port_description.h
    const char* name;
    int isParameter;
    int isFromBus;                      // 1 = вхід (ExtU), 0 = вихід (ExtY)
    VARTYPE type;                       // unsigned short; VT_R8 = 5 (double)
    int width;
    void* address;                      // адреса значення всередині моделі
};
```

Хост записує входи і читає виходи **безпосередньо за `address`**. Окремих
функцій set/get немає.

Порядок роботи: `mdlCreate` → перелік портів → початкові входи →
`mdlStart` → цикл {входи → `mdlStep` → виходи} → `mdlDestroy`.

### Обмеження, які треба знати

- **Розрядність.** Поточні DLL зібрані під x86 (`link /MACHINE:X86`), тому
  64-битний процес їх не завантажить. Хост має працювати як 32-бітний (див.
  нижче). Інший варіант: додати x64-збірку в `build_vs2026.bat` (`vcvarsall
  x64`, `/MACHINE:X64`).
- **До 10 екземплярів** однієї DLL одночасно: стільки вміщує таблиця
  `arrBinds` у `<model>_s2g.c`. Одинадцятий `mdlCreate` поверне NULL.
- Якщо в моделі задано скінченний `StopTime`, після нього `mdlStep`
  перестає рахувати. Для DLL ставте `inf`.

## Запуск з C#

```csharp
using Simulator.Core.Kos;

using var heli = KosModel.Load(@"C:\DynamicsNEW\AK1_3\slx\out\SimCore\AK13Dynamics.dll");
heli["HEIGHT_TRUE_CG_INIT"] = 100;
heli["DYN_STEP_S"] = 0.0025;          // 400 Гц
heli.Start();
for (var i = 0; i < 4000; i++)
{
    heli["RLX"] = 0.0;                // органи керування
    heli.Step();
    var pitch = heli["pitch_deg"];
}
```

## Запуск з командного рядка

DLL моделей 32-бітні, тож CLI теж публікується під x86. Встановлювати
x86-runtime для цього не потрібно:

```powershell
dotnet publish managed/src/Simulator.Cli -c Release -r win-x86 --self-contained -o out/cli-x86

out/cli-x86/simulator-cli kos --dll C:\DynamicsNEW\AK1_3\slx\AK13Dynamics.dll --list
out/cli-x86/simulator-cli kos --dll DynamicsGorEl.dll `
    --set Initial_Height=100 --set Initial_Airspeed=29 --set aircraft_mass_kg=11 `
    --duration 30 --outputs roll_deg,pitch_deg,AltitudeMSL_m,trueAirspeed_kph --out gorel.csv
```

`--set` задає входи перед стартом. Під час прогону вони не змінюються, тож
для сценаріїв зі зміною керування використовуйте C# API.

## Тести

`native/tests/kos_fake_model.c` — тестова DLL з тим самим ABI (точкова маса,
3 входи / 3 виходи, вектор шириною 3). На ній `KosModelTests` перевіряють
перелік портів, запис і читання за адресами, кілька незалежних екземплярів і
перевірку розрядності на будь-якій ОС, без MATLAB.

## Генерація коду: зауваження до логів

- `slbuild` завершується з «Failed to build / No supported compiler», і це
  очікувано: код уже згенеровано, а DLL компілює Visual Studio
  (`build_vs2026.bat`). `ak13_gen_code.m` правильно відділяє цю помилку від
  справжніх і пише `gen_code.done` = `OK`.
- Кожна генерація триває 4–5 хв з нуля, бо `build_dll.bat` видаляє
  `AK13Dynamics_dll_malloc_rtw`, а обірвана збірка не зберігає `.slxc`.
  Варіант: `set_param(mdl,'GenCodeOnly','on')` в `ak13_gen_code.m` і не
  видаляти папку. Перед цим треба перевірити, що таргет `kos_simulation` і
  тоді створює `_dll_malloc_main.c`, `_ports.c`, `_s2g.c` і
  `_mdlinterface.def`.
