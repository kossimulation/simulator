# Collects Simulink Embedded Coder output placed in native/generated/<model>/
# by matlab/codegen/generate_code.m. Enabled with -DSIMCORE_WITH_GENERATED=ON.

enable_language(C)

set(SIMCORE_GENERATED_SOURCES "")
set(SIMCORE_GENERATED_INCLUDE_DIRS "")

file(GLOB _simcore_models LIST_DIRECTORIES true "${CMAKE_CURRENT_LIST_DIR}/*")
foreach(_dir ${_simcore_models})
  if(IS_DIRECTORY "${_dir}")
    file(GLOB _srcs "${_dir}/*.cpp" "${_dir}/*.c")
    list(FILTER _srcs EXCLUDE REGEX "ert_main\\.(c|cpp)$")
    if(_srcs)
      get_filename_component(_name "${_dir}" NAME)
      message(STATUS "simcore: including generated model '${_name}'")
      list(APPEND SIMCORE_GENERATED_SOURCES ${_srcs})
      list(APPEND SIMCORE_GENERATED_INCLUDE_DIRS "${_dir}")
    endif()
  endif()
endforeach()

if(NOT SIMCORE_GENERATED_SOURCES)
  message(WARNING "SIMCORE_WITH_GENERATED=ON but native/generated/ contains no model sources")
endif()
