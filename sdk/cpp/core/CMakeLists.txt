# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The core module: the types every module shares (the library version, the time types, Point, Rectangle and ByteSpanUtil).

add_library(mb_framepacing_core STATIC)
add_library(mb_framepacing::core ALIAS mb_framepacing_core)
# Installed copies export the same name as the alias: find_package(mb_framepacing) gives mb_framepacing::core too
set_target_properties(mb_framepacing_core PROPERTIES EXPORT_NAME core POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_core
  PRIVATE
    source/mb/framepacing/core/GetLibraryVersion.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include ${MB_FRAMEPACING_GENERATED_INCLUDE_DIR}
    FILES
      include/mb/framepacing/core/ByteSpanUtil.hpp
      include/mb/framepacing/core/GetLibraryVersion.hpp
      include/mb/framepacing/core/LibraryVersion.hpp
      include/mb/framepacing/core/Point.hpp
      include/mb/framepacing/core/Rectangle.hpp
      include/mb/framepacing/core/time/ChronoConversion.hpp
      include/mb/framepacing/core/time/TickCount32.hpp
      include/mb/framepacing/core/time/TickCount64.hpp
      include/mb/framepacing/core/time/TimeSpan.hpp
      include/mb/framepacing/core/time/TimeSpan32.hpp
      ${MB_FRAMEPACING_GENERATED_INCLUDE_DIR}/mb/framepacing/core/Version.hpp)
target_compile_features(mb_framepacing_core PUBLIC cxx_std_20)
target_compile_options(mb_framepacing_core PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})

install(TARGETS mb_framepacing_core EXPORT mb_framepacingTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

if(MB_FRAMEPACING_BUILD_TESTS)
  add_executable(mb_framepacing_core_tests
    tests/AllocationTests.cpp
    tests/ByteSpanUtilTests.cpp
    tests/CoreTests.cpp
    tests/time/ChronoConversionTests.cpp
    tests/time/TickCount32Tests.cpp
    tests/time/TickCount64Tests.cpp
    tests/time/TimeSpan32Tests.cpp
    tests/time/TimeSpanTests.cpp)
  target_link_libraries(mb_framepacing_core_tests PRIVATE mb_framepacing::core mb_framepacing_test_support GTest::gtest_main)
  target_compile_options(mb_framepacing_core_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  target_compile_definitions(mb_framepacing_core_tests PRIVATE MB_FRAMEPACING_EXPECTED_VERSION="${MB_FRAMEPACING_VERSION}")
  gtest_discover_tests(mb_framepacing_core_tests)
endif()
