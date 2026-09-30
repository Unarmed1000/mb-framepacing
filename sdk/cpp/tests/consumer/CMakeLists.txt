# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# A minimal project that uses mb_framepacing the ways doc/integrating.md describes. check_consumers.py builds and runs it in CI.
#   MB_CONSUMER_MODE=fetchcontent  FetchContent with URL + URL_HASH (the release archive); FETCHCONTENT_SOURCE_DIR_MB_FRAMEPACING may
#                                  point at a local source tree instead
#   MB_CONSUMER_MODE=subdirectory  add_subdirectory(MB_FRAMEPACING_SOURCE_DIR)
#   MB_CONSUMER_MODE=package       find_package with the components it uses, after cmake --install (CMAKE_PREFIX_PATH)
cmake_minimum_required(VERSION 4.0)
project(mb_framepacing_consumer LANGUAGES CXX)

set(MB_CONSUMER_MODE "fetchcontent" CACHE STRING "fetchcontent, subdirectory or package")
set(MB_CONSUMER_VERSION "0.1" CACHE STRING "The mb_framepacing version to ask for")
set(MB_FRAMEPACING_URL "https://github.com/Unarmed1000/mb-framepacing/releases/download/sdk-v0.1.0/mb-framepacing-cpp-0.1.0.tar.gz" CACHE STRING "")
set(MB_FRAMEPACING_SHA256 "0000000000000000000000000000000000000000000000000000000000000000" CACHE STRING "")
set(MB_FRAMEPACING_SOURCE_DIR "" CACHE PATH "Source tree for the subdirectory mode")

if(MB_CONSUMER_MODE STREQUAL "subdirectory")
  add_subdirectory(${MB_FRAMEPACING_SOURCE_DIR} mb_framepacing)
elseif(MB_CONSUMER_MODE STREQUAL "package")
  find_package(mb_framepacing ${MB_CONSUMER_VERSION} CONFIG REQUIRED COMPONENTS marker data)
else()
  include(FetchContent)
  FetchContent_Declare(mb_framepacing
    URL ${MB_FRAMEPACING_URL}
    URL_HASH SHA256=${MB_FRAMEPACING_SHA256}
    FIND_PACKAGE_ARGS ${MB_CONSUMER_VERSION} CONFIG COMPONENTS marker data)
  FetchContent_MakeAvailable(mb_framepacing)
endif()

add_executable(consumer main.cpp)
target_link_libraries(consumer PRIVATE mb_framepacing::marker mb_framepacing::data)
