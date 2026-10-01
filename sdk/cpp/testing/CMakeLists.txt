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
