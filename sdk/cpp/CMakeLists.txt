# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
cmake_minimum_required(VERSION 4.0)

# mb_framepacing: one library of modules, each its own static library target, like Boost's and Poco's
#   core    mb_framepacing::core    shared types: the library version, tick units, Rectangle
#   marker  mb_framepacing::marker  the frame marker: encode it and draw it (links core)
#   data    mb_framepacing::data    reads the tools' capture data and analysis output (links marker; nlohmann/json inside)
#   pacer   mb_framepacing::pacer   paces frames with an adaptive swap interval, aligns the animation timer to refreshes (links core)
# Every module has its folder (include/, source/, tests/); the headers install into one tree, <mb/framepacing/...>.

# Inside the repository the version and LICENSE live in sdk/ (shared with the C#, Python and Unity libraries), the reference shaders in
# sdk/shaders and the third-party license texts in the repository root's licenses/. A standalone release archive carries them all next to
# this file instead.
if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/VERSION")
  set(MB_FRAMEPACING_META_DIR "${CMAKE_CURRENT_LIST_DIR}")
  set(MB_FRAMEPACING_LEGAL_DIR "${CMAKE_CURRENT_LIST_DIR}")
else()
  set(MB_FRAMEPACING_META_DIR "${CMAKE_CURRENT_LIST_DIR}/..")
  set(MB_FRAMEPACING_LEGAL_DIR "${CMAKE_CURRENT_LIST_DIR}/../..")
endif()

# Edit sdk/VERSION to bump the SDK (an edit reconfigures), or build one tree as another version with -DMB_FRAMEPACING_VERSION_OVERRIDE=x.y.z.
# A pre-release adds -alpha.N, -beta.N or -rc.N (see doc/releasing.md); CMake's project version is the numbers alone.
file(STRINGS "${MB_FRAMEPACING_META_DIR}/VERSION" MB_FRAMEPACING_VERSION_FILE LIMIT_COUNT 1)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${MB_FRAMEPACING_META_DIR}/VERSION")
set(MB_FRAMEPACING_VERSION_OVERRIDE "" CACHE STRING "Build as this mb_framepacing version instead of VERSION's (empty: VERSION)")
# A normal variable, read on every configure: it also hides a cached value from an older configure
set(MB_FRAMEPACING_VERSION "${MB_FRAMEPACING_VERSION_FILE}")
if(MB_FRAMEPACING_VERSION_OVERRIDE)
  set(MB_FRAMEPACING_VERSION "${MB_FRAMEPACING_VERSION_OVERRIDE}")
endif()
if(NOT MB_FRAMEPACING_VERSION MATCHES "^([0-9]+\\.[0-9]+\\.[0-9]+)(-((alpha|beta|rc)\\.[1-9][0-9]*))?$")
  message(FATAL_ERROR "MB_FRAMEPACING_VERSION '${MB_FRAMEPACING_VERSION}' is not MAJOR.MINOR.PATCH, optionally with -alpha.N, -beta.N or -rc.N")
endif()
set(MB_FRAMEPACING_VERSION_NUMBERS "${CMAKE_MATCH_1}")
set(MB_FRAMEPACING_VERSION_PRERELEASE "${CMAKE_MATCH_3}")

project(mb_framepacing VERSION ${MB_FRAMEPACING_VERSION_NUMBERS} DESCRIPTION "The mb-framepacing SDK: frame marker, data and frame pacer" LANGUAGES C CXX)

# The core is always built; the other modules can be left out. Tests and tools only build by default when this is the top level
# project, so engines that add_subdirectory/FetchContent the library never download GoogleTest.
option(MB_FRAMEPACING_BUILD_MARKER "Build the marker module (mb_framepacing::marker)" ON)
option(MB_FRAMEPACING_BUILD_DATA "Build the data module (mb_framepacing::data; needs the marker module, fetches nlohmann/json unless one is installed)" ON)
# The pacer is off until it is reworked (its time inputs and the Q32 grid need to be made right first)
option(MB_FRAMEPACING_BUILD_PACER "Build the pacer module (mb_framepacing::pacer); off until it is reworked" OFF)
option(MB_FRAMEPACING_BUILD_TESTS "Build the unit tests (fetches GoogleTest unless one is installed)" ${PROJECT_IS_TOP_LEVEL})
option(MB_FRAMEPACING_BUILD_TOOLS "Build the tools (marker-render)" ${PROJECT_IS_TOP_LEVEL})
option(MB_FRAMEPACING_WARNINGS_AS_ERRORS "Treat warnings as errors" ${PROJECT_IS_TOP_LEVEL})

if(MB_FRAMEPACING_BUILD_DATA AND NOT MB_FRAMEPACING_BUILD_MARKER)
  message(FATAL_ERROR "The data module needs the marker module: MB_FRAMEPACING_BUILD_DATA=ON needs MB_FRAMEPACING_BUILD_MARKER=ON")
endif()

include(GNUInstallDirs)
include(CMakePackageConfigHelpers)

# Warnings apply to our C++ sources only; vendored C sources (qrcodegen) are compiled as-is.
set(MB_FRAMEPACING_CXX_WARNINGS
    $<$<CXX_COMPILER_ID:MSVC>:/W4 /permissive- $<$<BOOL:${MB_FRAMEPACING_WARNINGS_AS_ERRORS}>:/WX>>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-Wall -Wextra -Wpedantic -Wconversion -Wshadow $<$<BOOL:${MB_FRAMEPACING_WARNINGS_AS_ERRORS}>:-Werror>>)

# The generated headers (core's Version.hpp) go here; the core target adds it to its header file set.
set(MB_FRAMEPACING_GENERATED_INCLUDE_DIR "${CMAKE_CURRENT_BINARY_DIR}/include")
configure_file(cmake/Version.hpp.in ${MB_FRAMEPACING_GENERATED_INCLUDE_DIR}/mb/framepacing/core/Version.hpp @ONLY)

if(MB_FRAMEPACING_BUILD_TESTS)
  enable_testing()
  include(FetchContent)
  # An installed GoogleTest (system, Conan, vcpkg) is used when find_package finds one, otherwise the pinned release is downloaded.
  FetchContent_Declare(googletest
    URL https://github.com/google/googletest/releases/download/v1.17.0/googletest-1.17.0.tar.gz
    URL_HASH SHA256=65fab701d9829d38cb77c14acdc431d2108bfdbf8979e40eb8ae567edf10b27c
    EXCLUDE_FROM_ALL
    SYSTEM
    FIND_PACKAGE_ARGS NAMES GTest CONFIG)
  set(gtest_force_shared_crt ON CACHE BOOL "Use the shared MSVC runtime, like the rest of the project" FORCE)
  set(INSTALL_GTEST OFF CACHE BOOL "Never install GoogleTest with mb_framepacing" FORCE)
  set(BUILD_GMOCK OFF CACHE BOOL "gmock is not used" FORCE)
  FetchContent_MakeAvailable(googletest)
  include(GoogleTest)
endif()

# Every module installs its target into this export set: find_package(mb_framepacing CONFIG COMPONENTS ...) gives mb_framepacing::<module>.
add_subdirectory(core)
set(MB_FRAMEPACING_COMPONENTS core)
if(MB_FRAMEPACING_BUILD_MARKER)
  add_subdirectory(marker)
  list(APPEND MB_FRAMEPACING_COMPONENTS marker)
endif()
if(MB_FRAMEPACING_BUILD_DATA)
  add_subdirectory(data)
  list(APPEND MB_FRAMEPACING_COMPONENTS data)
endif()
if(MB_FRAMEPACING_BUILD_PACER)
  add_subdirectory(pacer)
  list(APPEND MB_FRAMEPACING_COMPONENTS pacer)
endif()

# Install + export so projects can use find_package(mb_framepacing CONFIG) as an alternative to add_subdirectory/FetchContent.
install(FILES ${MB_FRAMEPACING_META_DIR}/LICENSE DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacing)
install(EXPORT mb_framepacingTargets NAMESPACE mb_framepacing:: DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framepacing)
configure_package_config_file(cmake/mb_framepacingConfig.cmake.in
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingConfig.cmake
  INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framepacing)
# Semantic versioning: in 0.x a minor release may break the API, from 1.0 only a major release may.
if(PROJECT_VERSION_MAJOR EQUAL 0)
  set(MB_FRAMEPACING_COMPATIBILITY SameMinorVersion)
else()
  set(MB_FRAMEPACING_COMPATIBILITY SameMajorVersion)
endif()
write_basic_package_version_file(${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingConfigVersion.cmake COMPATIBILITY ${MB_FRAMEPACING_COMPATIBILITY})
install(FILES
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingConfig.cmake
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingConfigVersion.cmake
  DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framepacing)
