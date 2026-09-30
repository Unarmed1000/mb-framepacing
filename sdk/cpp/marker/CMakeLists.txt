# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The marker module: encodes the frame marker (a QR code) and draws it as pixel aligned geometry or a bitmap. No dependencies (qrcodegen
# is vendored), no allocations.

add_library(mb_framepacing_marker STATIC)
add_library(mb_framepacing::marker ALIAS mb_framepacing_marker)
set_target_properties(mb_framepacing_marker PROPERTIES EXPORT_NAME marker C_STANDARD 11 C_STANDARD_REQUIRED ON POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_marker
  PRIVATE
    src/FrameMarker.cpp
    src/Payload.cpp
    third_party/qrcodegen/qrcodegen.c
    third_party/qrcodegen/qrcodegen.h
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/Marker.hpp
      include/mb/framepacing/marker/Constants.hpp
      include/mb/framepacing/marker/IndexedCount.hpp
      include/mb/framepacing/marker/MarkerFlags.hpp
      include/mb/framepacing/marker/MarkerKind.hpp
      include/mb/framepacing/marker/ModuleMatrix.hpp
      include/mb/framepacing/marker/Options.hpp
      include/mb/framepacing/marker/Payload.hpp
      include/mb/framepacing/marker/PixelFormat.hpp
      include/mb/framepacing/marker/Point.hpp
      include/mb/framepacing/marker/QuadRect.hpp
      include/mb/framepacing/marker/SequenceId.hpp
      include/mb/framepacing/marker/StartMetadata.hpp
      include/mb/framepacing/marker/Vertex.hpp)
target_compile_features(mb_framepacing_marker PUBLIC cxx_std_20)
target_link_libraries(mb_framepacing_marker PUBLIC mb_framepacing::core)
target_include_directories(mb_framepacing_marker PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/third_party/qrcodegen)
target_compile_options(mb_framepacing_marker PRIVATE $<$<COMPILE_LANGUAGE:CXX>:${MB_FRAMEPACING_CXX_WARNINGS}>)

install(TARGETS mb_framepacing_marker EXPORT mb_framepacingTargets
  ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
  FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
# The one third-party component compiled into the module (qrcodegen), and the reference shaders that draw the marker as one quad
# (sdk/shaders; shaders/ in a release archive)
install(FILES ${MB_FRAMEPACING_LEGAL_DIR}/licenses/qrcodegen-MIT.txt DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacing/licenses)
install(DIRECTORY ${MB_FRAMEPACING_META_DIR}/shaders/ DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacing/shaders)

if(MB_FRAMEPACING_BUILD_TOOLS)
  add_executable(marker-render tools/marker-render/main.cpp)
  target_link_libraries(marker-render PRIVATE mb_framepacing::marker)
  target_compile_options(marker-render PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
endif()

if(MB_FRAMEPACING_BUILD_TESTS)
  add_executable(mb_framepacing_marker_tests tests/FrameMarkerTests.cpp tests/AllocationTests.cpp)
  target_link_libraries(mb_framepacing_marker_tests PRIVATE mb_framepacing::marker GTest::gtest_main)
  target_compile_options(mb_framepacing_marker_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  gtest_discover_tests(mb_framepacing_marker_tests)
endif()
