# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
cmake_minimum_required(VERSION 3.15)
project(test_package LANGUAGES CXX)

# The test package's conanfile.py sets it when the package was built with with_pacer (the experimental pacer module)
option(MB_TEST_PACKAGE_PACER "The package has the pacer module (mb_framepacing::pacer)" OFF)
set(MB_TEST_PACKAGE_COMPONENTS marker data)
if(MB_TEST_PACKAGE_PACER)
  list(APPEND MB_TEST_PACKAGE_COMPONENTS pacer)
endif()

find_package(mb_framepacing CONFIG REQUIRED COMPONENTS ${MB_TEST_PACKAGE_COMPONENTS})

add_executable(test_package test_package.cpp)
target_link_libraries(test_package PRIVATE mb_framepacing::marker mb_framepacing::data)
if(MB_TEST_PACKAGE_PACER)
  target_link_libraries(test_package PRIVATE mb_framepacing::pacer)
  target_compile_definitions(test_package PRIVATE MB_TEST_PACKAGE_PACER)
endif()
target_compile_features(test_package PRIVATE cxx_std_20)
