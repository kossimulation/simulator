# Згенерований код Simulink

Сюди `matlab/codegen/generate_code.m` розпаковує C++ код, згенерований
Embedded Coder, по одній папці на модель:

```
native/generated/
  heli_rotor/        <- generate_code('heli_rotor')
    heli_rotor.cpp
    heli_rotor.h
    rtwtypes.h
    ...
```

Збірка ядра разом зі згенерованим кодом:

```
cmake -S native -B build/native -DSIMCORE_WITH_GENERATED=ON
cmake --build build/native --config Release
```

Код у цій папці **генерується** і вручну не редагується. Чи комітити його,
вирішуйте свідомо: комітьте, якщо збірка має працювати без MATLAB
(наприклад, у CI), і не комітьте, якщо MATLAB є в усіх розробників.
Як підключити модель до симуляції, описано в `docs/architecture.md`.
