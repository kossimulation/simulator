cmake_minimum_required(VERSION 3.20)
project(simcore VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

# Put the shared library where the C# projects look for it (build/native/bin),
# also for multi-config generators such as Visual Studio.
set(SIMCORE_OUTPUT_DIR ${CMAKE_BINARY_DIR}/bin)
foreach(cfg "" _DEBUG _RELEASE _RELWITHDEBINFO _MINSIZEREL)
  set(CMAKE_RUNTIME_OUTPUT_DIRECTORY${cfg} ${SIMCORE_OUTPUT_DIR})
  set(CMAKE_LIBRARY_OUTPUT_DIRECTORY${cfg} ${SIMCORE_OUTPUT_DIR})
endforeach()

option(SIMCORE_BUILD_TESTS "Build native unit tests" ON)
option(SIMCORE_WITH_GENERATED "Compile Simulink Embedded Coder output from generated/" OFF)

set(SIMCORE_SOURCES
  src/atmosphere.cpp
  src/rigid_body.cpp
  src/vehicle.cpp
  src/fixed_wing.cpp
  src/helicopter.cpp
  src/simulation.cpp
  src/c_api.cpp
)

if(SIMCORE_WITH_GENERATED)
  include(generated/generated.cmake)
endif()

if(MSVC)
  set(SIMCORE_WARNINGS /W4 /permissive-)
else()
  set(SIMCORE_WARNINGS -Wall -Wextra -Wpedantic -Wshadow)
endif()

# Static library: used by native tests and anything linking the C++ API.
add_library(simcore_static STATIC ${SIMCORE_SOURCES})
target_include_directories(simcore_static
  PUBLIC include
  PUBLIC src)
target_compile_definitions(simcore_static PUBLIC SIMCORE_STATIC)
target_compile_options(simcore_static PRIVATE ${SIMCORE_WARNINGS})

# Shared library: loaded by the C# layer through P/Invoke.
add_library(simcore SHARED ${SIMCORE_SOURCES})
target_include_directories(simcore PUBLIC include PRIVATE src)
target_compile_definitions(simcore PRIVATE SIMCORE_BUILDING)
target_compile_options(simcore PRIVATE ${SIMCORE_WARNINGS})
set_target_properties(simcore PROPERTIES
  CXX_VISIBILITY_PRESET hidden
  VISIBILITY_INLINES_HIDDEN ON)

if(SIMCORE_WITH_GENERATED)
  foreach(t simcore simcore_static)
    target_sources(${t} PRIVATE ${SIMCORE_GENERATED_SOURCES})
    target_include_directories(${t} PRIVATE ${SIMCORE_GENERATED_INCLUDE_DIRS})
  endforeach()
endif()

if(SIMCORE_BUILD_TESTS)
  enable_testing()
  add_executable(simcore_tests tests/test_main.cpp)
  target_link_libraries(simcore_tests PRIVATE simcore_static)
  target_compile_options(simcore_tests PRIVATE ${SIMCORE_WARNINGS})
  add_test(NAME simcore_tests COMMAND simcore_tests)

  # Stand-in for kos_simulation model DLLs, used by the managed KosModel tests.
  add_library(kos_fake_model SHARED tests/kos_fake_model.c)
  set_target_properties(kos_fake_model PROPERTIES C_VISIBILITY_PRESET hidden)
endif()
