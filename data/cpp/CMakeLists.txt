# SPDX-License-Identifier: BSD-3-Clause
cmake_minimum_required(VERSION 4.0)

# Inside the repository the version and LICENSE live in data/ (shared with the C# and Python data libraries) and the third-party license
# texts in the repository root's licenses/. A standalone release archive carries them all next to this file instead.
if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/VERSION")
  set(MB_FRAMEPACINGDATA_META_DIR "${CMAKE_CURRENT_LIST_DIR}")
  set(MB_FRAMEPACINGDATA_LEGAL_DIR "${CMAKE_CURRENT_LIST_DIR}")
else()
  set(MB_FRAMEPACINGDATA_META_DIR "${CMAKE_CURRENT_LIST_DIR}/..")
  set(MB_FRAMEPACINGDATA_LEGAL_DIR "${CMAKE_CURRENT_LIST_DIR}/../..")
endif()

# Edit data/VERSION to bump the data libraries, or override with -DMB_FRAMEPACINGDATA_VERSION=x.y.z.
file(STRINGS "${MB_FRAMEPACINGDATA_META_DIR}/VERSION" MB_FRAMEPACINGDATA_VERSION_FILE LIMIT_COUNT 1)
set(MB_FRAMEPACINGDATA_VERSION "${MB_FRAMEPACINGDATA_VERSION_FILE}" CACHE STRING "mb_framepacingdata version (defaults to data/VERSION)")
if(NOT MB_FRAMEPACINGDATA_VERSION MATCHES "^[0-9]+\.[0-9]+\.[0-9]+$")
  message(FATAL_ERROR "MB_FRAMEPACINGDATA_VERSION '${MB_FRAMEPACINGDATA_VERSION}' is not MAJOR.MINOR.PATCH")
endif()

project(mb_framepacingdata VERSION ${MB_FRAMEPACINGDATA_VERSION} DESCRIPTION "Reads the mb-framepacing tools' capture data and analysis output" LANGUAGES C CXX)

option(MB_FRAMEPACINGDATA_BUILD_TESTS "Build the mb_framepacingdata unit tests (fetches GoogleTest unless one is installed)" ${PROJECT_IS_TOP_LEVEL})
option(MB_FRAMEPACINGDATA_WARNINGS_AS_ERRORS "Treat warnings as errors" ${PROJECT_IS_TOP_LEVEL})

set(MB_FRAMEPACINGDATA_CXX_WARNINGS
    $<$<CXX_COMPILER_ID:MSVC>:/W4 /permissive- $<$<BOOL:${MB_FRAMEPACINGDATA_WARNINGS_AS_ERRORS}>:/WX>>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-Wall -Wextra -Wpedantic -Wconversion -Wshadow $<$<BOOL:${MB_FRAMEPACINGDATA_WARNINGS_AS_ERRORS}>:-Werror>>)

include(FetchContent)
include(GNUInstallDirs)

# The marker library version this one needs (0.x: the same minor version)
set(MB_FRAMEPACINGDATA_MARKER_VERSION 0.1)

# The marker library decodes the markers' payloads. A project that already has it (add_subdirectory, FetchContent) keeps its copy; inside
# the repository it is marker/cpp, otherwise an installed one.
if(NOT TARGET mb::framemarker)
  if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/../../marker/cpp/CMakeLists.txt")
    add_subdirectory("${CMAKE_CURRENT_LIST_DIR}/../../marker/cpp" "${CMAKE_CURRENT_BINARY_DIR}/mb_framemarker" EXCLUDE_FROM_ALL)
  else()
    find_package(mb_framemarker ${MB_FRAMEPACINGDATA_MARKER_VERSION} CONFIG REQUIRED)
  endif()
endif()

# JSON for summary.json, used inside the library only (header only, not part of the API). An installed copy wins.
FetchContent_Declare(nlohmann_json
  URL https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz
  URL_HASH SHA256=42f6e95cad6ec532fd372391373363b62a14af6d771056dbfc86160e6dfff7aa
  EXCLUDE_FROM_ALL
  SYSTEM
  FIND_PACKAGE_ARGS 3.12 CONFIG)
FetchContent_MakeAvailable(nlohmann_json)

configure_file(cmake/Version.hpp.in ${CMAKE_CURRENT_BINARY_DIR}/include/mb/framepacingdata/Version.hpp @ONLY)

add_library(mb_framepacingdata STATIC)
add_library(mb::framepacingdata ALIAS mb_framepacingdata)
set_target_properties(mb_framepacingdata PROPERTIES EXPORT_NAME framepacingdata POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacingdata
  PRIVATE
    src/AnalysisOutput.cpp
    src/CaptureData.cpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include ${CMAKE_CURRENT_BINARY_DIR}/include
    FILES
      include/mb/framepacingdata/AnalysisSummary.hpp
      include/mb/framepacingdata/CaptureCsvRow.hpp
      include/mb/framepacingdata/CaptureDataHeader.hpp
      include/mb/framepacingdata/CaptureDataReader.hpp
      include/mb/framepacingdata/CaptureDataRecord.hpp
      include/mb/framepacingdata/CaptureDataStatus.hpp
      include/mb/framepacingdata/CaptureRecordFlags.hpp
      include/mb/framepacingdata/Constants.hpp
      include/mb/framepacingdata/DataFormatError.hpp
      include/mb/framepacingdata/DataRect.hpp
      include/mb/framepacingdata/FramePacingData.hpp
      include/mb/framepacingdata/FrameRow.hpp
      include/mb/framepacingdata/MarkerLocation.hpp
      include/mb/framepacingdata/SummaryCamera.hpp
      include/mb/framepacingdata/SummaryCounts.hpp
      include/mb/framepacingdata/SummaryHistogram.hpp
      include/mb/framepacingdata/SummaryHistogramBin.hpp
      include/mb/framepacingdata/SummaryHistograms.hpp
      include/mb/framepacingdata/SummaryMarker.hpp
      include/mb/framepacingdata/SummaryPacing.hpp
      include/mb/framepacingdata/SummaryRun.hpp
      include/mb/framepacingdata/SummaryStatistics.hpp
      include/mb/framepacingdata/ValueStatistics.hpp
      ${CMAKE_CURRENT_BINARY_DIR}/include/mb/framepacingdata/Version.hpp)
target_compile_features(mb_framepacingdata PUBLIC cxx_std_20)
target_link_libraries(mb_framepacingdata PUBLIC mb::framemarker PRIVATE $<BUILD_INTERFACE:nlohmann_json::nlohmann_json>)
target_compile_options(mb_framepacingdata PRIVATE ${MB_FRAMEPACINGDATA_CXX_WARNINGS})

if(MB_FRAMEPACINGDATA_BUILD_TESTS)
  enable_testing()
  FetchContent_Declare(googletest
    URL https://github.com/google/googletest/releases/download/v1.17.0/googletest-1.17.0.tar.gz
    URL_HASH SHA256=65fab701d9829d38cb77c14acdc431d2108bfdbf8979e40eb8ae567edf10b27c
    EXCLUDE_FROM_ALL
    SYSTEM
    FIND_PACKAGE_ARGS NAMES GTest CONFIG)
  set(gtest_force_shared_crt ON CACHE BOOL "Use the shared MSVC runtime, like the rest of the project" FORCE)
  set(INSTALL_GTEST OFF CACHE BOOL "Never install GoogleTest with mb_framepacingdata" FORCE)
  set(BUILD_GMOCK OFF CACHE BOOL "gmock is not used" FORCE)
  FetchContent_MakeAvailable(googletest)

  add_executable(mb_framepacingdata_tests tests/AnalysisOutputTests.cpp tests/CaptureDataTests.cpp tests/GoldenDataTests.cpp)
  target_link_libraries(mb_framepacingdata_tests PRIVATE mb::framepacingdata nlohmann_json::nlohmann_json GTest::gtest_main)
  target_compile_options(mb_framepacingdata_tests PRIVATE ${MB_FRAMEPACINGDATA_CXX_WARNINGS})
  # The golden data (test-data/data) is found above this folder; a copy outside the repository skips those tests
  target_compile_definitions(mb_framepacingdata_tests PRIVATE MB_FRAMEPACINGDATA_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}"
                                                              MB_FRAMEPACINGDATA_EXPECTED_VERSION="${PROJECT_VERSION}")
  include(GoogleTest)
  gtest_discover_tests(mb_framepacingdata_tests)
endif()

# Install + export so tools can use find_package(mb_framepacingdata CONFIG); it finds the marker library the same way.
include(CMakePackageConfigHelpers)
install(TARGETS mb_framepacingdata EXPORT mb_framepacingdataTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
# The library's own license and the one third-party component compiled into it (nlohmann/json)
install(FILES ${MB_FRAMEPACINGDATA_META_DIR}/LICENSE DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacingdata)
install(FILES ${MB_FRAMEPACINGDATA_LEGAL_DIR}/licenses/nlohmann-json-MIT.txt DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacingdata/licenses)
install(EXPORT mb_framepacingdataTargets NAMESPACE mb:: DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framepacingdata)
configure_package_config_file(cmake/mb_framepacingdataConfig.cmake.in
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingdataConfig.cmake
  INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framepacingdata)
# Semantic versioning: in 0.x a minor release may break the API, from 1.0 only a major release may.
if(PROJECT_VERSION_MAJOR EQUAL 0)
  set(MB_FRAMEPACINGDATA_COMPATIBILITY SameMinorVersion)
else()
  set(MB_FRAMEPACINGDATA_COMPATIBILITY SameMajorVersion)
endif()
write_basic_package_version_file(${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingdataConfigVersion.cmake COMPATIBILITY ${MB_FRAMEPACINGDATA_COMPATIBILITY})
install(FILES
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingdataConfig.cmake
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framepacingdataConfigVersion.cmake
  DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framepacingdata)
