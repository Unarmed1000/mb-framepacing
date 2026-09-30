# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The core module: the types every module shares (the library version, the tick units, Rectangle).

add_library(mb_framepacing_core STATIC)
add_library(mb_framepacing::core ALIAS mb_framepacing_core)
# Installed copies export the same name as the alias: find_package(mb_framepacing) gives mb_framepacing::core too
set_target_properties(mb_framepacing_core PROPERTIES EXPORT_NAME core POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_core
  PRIVATE
    src/LibraryVersion.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include ${MB_FRAMEPACING_GENERATED_INCLUDE_DIR}
    FILES
      include/mb/framepacing/Core.hpp
      include/mb/framepacing/core/LibraryVersion.hpp
      include/mb/framepacing/core/Rectangle.hpp
      include/mb/framepacing/core/Ticks.hpp
      ${MB_FRAMEPACING_GENERATED_INCLUDE_DIR}/mb/framepacing/core/Version.hpp)
target_compile_features(mb_framepacing_core PUBLIC cxx_std_20)
target_compile_options(mb_framepacing_core PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

install(TARGETS mb_framepacing_core EXPORT mb_framepacingTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

if(MB_FRAMEPACING_BUILD_TESTS)
  add_executable(mb_framepacing_core_tests tests/CoreTests.cpp)
  target_link_libraries(mb_framepacing_core_tests PRIVATE mb_framepacing::core GTest::gtest_main)
  target_compile_options(mb_framepacing_core_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  target_compile_definitions(mb_framepacing_core_tests PRIVATE MB_FRAMEPACING_EXPECTED_VERSION="${MB_FRAMEPACING_VERSION}")
  gtest_discover_tests(mb_framepacing_core_tests)
endif()
