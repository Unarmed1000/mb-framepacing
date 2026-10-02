# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# Test support shared by the modules' tests: built with the tests only, never installed or exported. An OBJECT library, so the counting
# replacements of the global operator new and delete are always linked into the test executable that uses it.

add_library(mb_framepacing_test_support OBJECT)
target_sources(mb_framepacing_test_support
  PRIVATE
    source/mb/framepacing/testing/AllocationCounter.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/testing/AllocationCounter.hpp)
target_compile_features(mb_framepacing_test_support PUBLIC cxx_std_20)
target_compile_options(mb_framepacing_test_support PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

# Every public header of a module, compiled as an application has them that included X11's headers first: Xlib.h and X.h define plain
# words as macros (None, Status, Bool, Success, Always, ...), so the SDK's headers must not use those words as names. Adds a generated
# source to the module's test executable; it only has to compile.
function(mb_framepacing_add_x11_macro_check target module)
  set(include_dir "${CMAKE_CURRENT_SOURCE_DIR}/include")
  file(GLOB_RECURSE headers CONFIGURE_DEPENDS RELATIVE "${include_dir}" "${include_dir}/*.hpp")
  list(SORT headers)
  set(MB_FRAMEPACING_CHECKED_MODULE "${module}")
  set(MB_FRAMEPACING_CHECKED_INCLUDES "")
  foreach(header IN LISTS headers)
    string(APPEND MB_FRAMEPACING_CHECKED_INCLUDES "#include <${header}>
")
  endforeach()
  configure_file("${CMAKE_CURRENT_FUNCTION_LIST_DIR}/X11Macros.cpp.in" "${CMAKE_CURRENT_BINARY_DIR}/X11Macros.cpp" @ONLY)
  target_sources(${target} PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/X11Macros.cpp")
endfunction()
