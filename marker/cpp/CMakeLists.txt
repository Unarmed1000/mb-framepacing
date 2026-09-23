cmake_minimum_required(VERSION 4.0)

# Inside the repository the version, LICENSE and license texts live above this directory (marker/VERSION, shared with the C# marker
# library, and the repository root). A standalone release archive carries them next to this file instead.
if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/VERSION")
  set(MB_FRAMEMARKER_META_DIR "${CMAKE_CURRENT_LIST_DIR}")
  set(MB_FRAMEMARKER_LEGAL_DIR "${CMAKE_CURRENT_LIST_DIR}")
else()
  set(MB_FRAMEMARKER_META_DIR "${CMAKE_CURRENT_LIST_DIR}/..")
  set(MB_FRAMEMARKER_LEGAL_DIR "${CMAKE_CURRENT_LIST_DIR}/../..")
endif()

# Edit marker/VERSION to bump the marker libraries, or override with -DMB_FRAMEMARKER_VERSION=x.y.z.
file(STRINGS "${MB_FRAMEMARKER_META_DIR}/VERSION" MB_FRAMEMARKER_VERSION_FILE LIMIT_COUNT 1)
set(MB_FRAMEMARKER_VERSION "${MB_FRAMEMARKER_VERSION_FILE}" CACHE STRING "mb_framemarker version (defaults to marker/VERSION)")

project(mb_framemarker VERSION ${MB_FRAMEMARKER_VERSION} DESCRIPTION "Pixel aligned QR frame marker geometry" LANGUAGES C CXX)

# Tests and tools only build by default when this is the top level project, so engines that add_subdirectory/FetchContent the library
# never download GoogleTest.
option(MB_FRAMEMARKER_BUILD_TESTS "Build the mb_framemarker unit tests (fetches GoogleTest unless one is installed)" ${PROJECT_IS_TOP_LEVEL})
option(MB_FRAMEMARKER_BUILD_TOOLS "Build marker-render" ${PROJECT_IS_TOP_LEVEL})
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
    FILES
      include/mb/framemarker/Constants.hpp
      include/mb/framemarker/FrameMarker.hpp
      include/mb/framemarker/IndexedCount.hpp
      include/mb/framemarker/MarkerKind.hpp
      include/mb/framemarker/MarkerSlot.hpp
      include/mb/framemarker/ModuleMatrix.hpp
      include/mb/framemarker/Options.hpp
      include/mb/framemarker/Payload.hpp
      include/mb/framemarker/Point.hpp
      include/mb/framemarker/Quad.hpp
      include/mb/framemarker/StartMetadata.hpp
      include/mb/framemarker/Vertex.hpp
      ${CMAKE_CURRENT_BINARY_DIR}/include/mb/framemarker/Version.hpp)
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

# Install + export so engines can use find_package(mb_framemarker CONFIG) as an alternative to add_subdirectory/FetchContent.
include(CMakePackageConfigHelpers)
install(TARGETS mb_framemarker EXPORT mb_framemarkerTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
# The library's own license and the one third-party component compiled into it (qrcodegen)
install(FILES ${MB_FRAMEMARKER_LEGAL_DIR}/LICENSE DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framemarker)
install(FILES ${MB_FRAMEMARKER_LEGAL_DIR}/licenses/qrcodegen-MIT.txt DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framemarker/licenses)
install(EXPORT mb_framemarkerTargets NAMESPACE mb:: DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framemarker)
configure_package_config_file(cmake/mb_framemarkerConfig.cmake.in
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfig.cmake
  INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framemarker)
# Semantic versioning: in 0.x a minor release may break the API, from 1.0 only a major release may.
if(PROJECT_VERSION_MAJOR EQUAL 0)
  set(MB_FRAMEMARKER_COMPATIBILITY SameMinorVersion)
else()
  set(MB_FRAMEMARKER_COMPATIBILITY SameMajorVersion)
endif()
write_basic_package_version_file(${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfigVersion.cmake COMPATIBILITY ${MB_FRAMEMARKER_COMPATIBILITY})
install(FILES
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfig.cmake
  ${CMAKE_CURRENT_BINARY_DIR}/mb_framemarkerConfigVersion.cmake
  DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/mb_framemarker)
