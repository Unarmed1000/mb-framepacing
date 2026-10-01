# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# Size probes: tiny programs that use one more module each, linked the way an application links for size (unused code dropped).
# tools/measure_sdk_size.py builds them and reports each one's size minus the baseline's: what the SDK adds to an executable.
cmake_minimum_required(VERSION 4.0)
project(mb_framepacing_size LANGUAGES C CXX)

set(MB_FRAMEPACING_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../.." CACHE PATH "The mb_framepacing source tree")
add_subdirectory(${MB_FRAMEPACING_SOURCE_DIR} mb_framepacing)

function(mb_framepacing_size_probe name source)
  add_executable(${name} ${source})
  target_link_libraries(${name} PRIVATE ${ARGN})
  target_compile_features(${name} PRIVATE cxx_std_20)
  # Let the linker drop what the program does not call: the library's objects are built with a section per function
  target_compile_options(${name} PRIVATE "$<$<NOT:$<STREQUAL:$<CXX_COMPILER_FRONTEND_VARIANT>,MSVC>>:-ffunction-sections;-fdata-sections>")
  target_link_options(${name} PRIVATE
    "$<$<STREQUAL:$<CXX_COMPILER_FRONTEND_VARIANT>,MSVC>:/OPT:REF;/OPT:ICF>"
    "$<$<AND:$<PLATFORM_ID:Darwin>,$<NOT:$<STREQUAL:$<CXX_COMPILER_FRONTEND_VARIANT>,MSVC>>>:-Wl,-dead_strip>"
    "$<$<AND:$<NOT:$<PLATFORM_ID:Darwin>>,$<NOT:$<STREQUAL:$<CXX_COMPILER_FRONTEND_VARIANT>,MSVC>>>:-Wl,--gc-sections>")
endfunction()

mb_framepacing_size_probe(size_baseline Baseline.cpp)
mb_framepacing_size_probe(size_core Core.cpp mb_framepacing::core)
mb_framepacing_size_probe(size_marker Marker.cpp mb_framepacing::marker)
mb_framepacing_size_probe(size_marker_data MarkerData.cpp mb_framepacing::marker mb_framepacing::data)
