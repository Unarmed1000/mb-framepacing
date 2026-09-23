cmake_minimum_required(VERSION 4.0)

# The version comes from the repository's VERSION file, shared with the .NET tools (dotnet/Directory.Build.props).
# Edit VERSION to bump everything, or override with -DMB_FRAMEMARKER_VERSION=x.y.z.
file(STRINGS "${CMAKE_CURRENT_LIST_DIR}/../VERSION" MB_FRAMEPACING_VERSION_FILE LIMIT_COUNT 1)
set(MB_FRAMEMARKER_VERSION "${MB_FRAMEPACING_VERSION_FILE}" CACHE STRING "mb_framemarker version (defaults to the VERSION file)")

project(mb_framemarker VERSION ${MB_FRAMEMARKER_VERSION} DESCRIPTION "Pixel aligned QR frame marker geometry" LANGUAGES C CXX)

# Tests and tools only build by default when this is the top level project, so engines that add_subdirectory/FetchContent the library
# never download GoogleTest.
option(MB_FRAMEMARKER_BUILD_TESTS "Build the mb_framemarker unit tests (fetches GoogleTest unless one is installed)" ${PROJECT_IS_TOP_LEVEL})
option(MB_FRAMEMARKER_BUILD_TOOLS "Build marker-render" ${PROJECT_IS_TOP_LEVEL})
option(MB_FRAMEMARKER_BUILD_SAMPLES "Build the SDL3 marker-display sample (downloads SDL3)" OFF)
option(MB_FRAMEMARKER_WARNINGS_AS_ERRORS "Treat warnings as errors" ${PROJECT_IS_TOP_LEVEL})

include(GNUInstallDirs)

# Warnings apply to our C++ sources only, the vendored qrcodegen C source is compiled as-is.
set(MB_FRAMEMARKER_CXX_WARNINGS
    $<$<CXX_COMPILER_ID:MSVC>:/W4 /permissive- $<$<BOOL:${MB_FRAMEMARKER_WARNINGS_AS_ERRORS}>:/WX>>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-Wall -Wextra -Wpedantic -Wconversion -Wshadow $<$<BOOL:${MB_FRAMEMARKER_WARNINGS_AS_ERRORS}>:-Werror>>)

configure_file(cmake/Version.hpp.in ${CMAKE_CURRENT_BINARY_DIR}/include/mb/framemarker/Version.hpp @ONLY)

add_library(mb_framemarker STATIC)
add_library(mb::framemarker ALIAS mb_framemarker)
target_sources(mb_framemarker
  PRIVATE
    src/FrameMarker.cpp
    src/Payload.cpp
    third_party/qrcodegen/qrcodegen.c
    third_party/qrcodegen/qrcodegen.h
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include ${CMAKE_CURRENT_BINARY_DIR}/include
    FILES include/mb/framemarker/FrameMarker.hpp ${CMAKE_CURRENT_BINARY_DIR}/include/mb/framemarker/Version.hpp)
target_compile_features(mb_framemarker PUBLIC cxx_std_20)
set_target_properties(mb_framemarker PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON POSITION_INDEPENDENT_CODE ON)
target_include_directories(mb_framemarker PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/third_party/qrcodegen)
target_compile_options(mb_framemarker PRIVATE $<$<COMPILE_LANGUAGE:CXX>:${MB_FRAMEMARKER_CXX_WARNINGS}>)

if(MB_FRAMEMARKER_BUILD_TOOLS)
  add_executable(marker-render tools/marker-render/main.cpp)
  target_link_libraries(marker-render PRIVATE mb::framemarker)
  target_compile_options(marker-render PRIVATE ${MB_FRAMEMARKER_CXX_WARNINGS})
endif()

if(MB_FRAMEMARKER_BUILD_TESTS)
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
  set(INSTALL_GTEST OFF CACHE BOOL "Never install GoogleTest with mb_framemarker" FORCE)
  set(BUILD_GMOCK OFF CACHE BOOL "gmock is not used" FORCE)
  FetchContent_MakeAvailable(googletest)

  add_executable(mb_framemarker_tests tests/FrameMarkerTests.cpp)
  target_link_libraries(mb_framemarker_tests PRIVATE mb::framemarker GTest::gtest_main)
  target_compile_options(mb_framemarker_tests PRIVATE ${MB_FRAMEMARKER_CXX_WARNINGS})
  target_compile_definitions(mb_framemarker_tests PRIVATE MB_FRAMEMARKER_EXPECTED_VERSION="${PROJECT_VERSION}")
  include(GoogleTest)
  gtest_discover_tests(mb_framemarker_tests)
endif()

if(MB_FRAMEMARKER_BUILD_SAMPLES)
  add_subdirectory(samples/marker-display)
endif()

# Install + export so engines can use find_package(mb_framemarker CONFIG) as an alternative to add_subdirectory/FetchContent.
include(CMakePackageConfigHelpers)
install(TARGETS mb_framemarker EXPORT mb_framemarkerTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(FILES ${CMAKE_CURRENT_SOURCE_DIR}/../LICENSE DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framemarker)
install(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/../licenses/ DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framemarker/licenses)
install(EXPORT mb_framemarkerTargets NAMESPACE mb:: DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framemarker)
configure_package_config_file(cmake/mb_framemarkerConfig.cmake.in
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfig.cmake
  INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framemarker)
write_basic_package_version_file(${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfigVersion.cmake COMPATIBILITY SameMajorVersion)
install(FILES
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfig.cmake
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfigVersion.cmake
  DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framemarker)
