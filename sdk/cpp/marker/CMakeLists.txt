# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
# The marker module: encodes the frame marker (a QR code) and draws it as pixel aligned geometry or a bitmap. No dependencies, no
# allocations. Its QR encoder is its own (source/mb/framepacing/marker/detail/QrEncoder.cpp); the QR Code generator library it is
# ported from stays vendored, outside the library (reference/third_party/qrcodegen): the tests compare the encoder with it and the
# benchmarks measure it against it.

add_library(mb_framepacing_marker STATIC)
add_library(mb_framepacing::marker ALIAS mb_framepacing_marker)
set_target_properties(mb_framepacing_marker PROPERTIES EXPORT_NAME marker POSITION_INDEPENDENT_CODE ON)
target_sources(mb_framepacing_marker
  PRIVATE
    source/mb/framepacing/marker/FrameMarker.cpp
    source/mb/framepacing/marker/detail/QrEncoder.cpp
    source/mb/framepacing/marker/detail/QrEncoder.hpp
    source/mb/framepacing/marker/detail/QrMaskPatterns.hpp
    source/mb/framepacing/marker/detail/QrReedSolomon.hpp
    source/mb/framepacing/marker/detail/QrSymbol.hpp
    source/mb/framepacing/marker/detail/QrVersionTables.hpp
    source/mb/framepacing/marker/detail/WireFormat.hpp
  PUBLIC
    FILE_SET HEADERS
    BASE_DIRS include
    FILES
      include/mb/framepacing/marker/FrameMarker.hpp
      include/mb/framepacing/marker/geometry/IndexedCount.hpp
      include/mb/framepacing/marker/payload/MarkerFlags.hpp
      include/mb/framepacing/marker/MarkerKind.hpp
      include/mb/framepacing/marker/geometry/ModuleMatrix.hpp
      include/mb/framepacing/marker/Options.hpp
      include/mb/framepacing/marker/payload/Payload.hpp
      include/mb/framepacing/marker/geometry/PixelFormat.hpp
      include/mb/framepacing/marker/geometry/PixelFormatUtil.hpp
      include/mb/framepacing/marker/geometry/MarkerQuad.hpp
      include/mb/framepacing/marker/payload/SequenceId.hpp
      include/mb/framepacing/marker/payload/StartMetadata.hpp
      include/mb/framepacing/marker/geometry/Vertex.hpp)
target_compile_features(mb_framepacing_marker PUBLIC cxx_std_20)
target_link_libraries(mb_framepacing_marker PUBLIC mb_framepacing::core)
target_compile_options(mb_framepacing_marker PRIVATE $<$<COMPILE_LANGUAGE:CXX>:${MB_FRAMEPACING_CXX_WARNINGS}> ${MB_FRAMEPACING_SECTION_FLAGS})

if(MB_FRAMEPACING_INSTALL)
  install(TARGETS mb_framepacing_marker EXPORT mb_framepacingTargets
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    FILE_SET HEADERS DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
  # The third-party component the module's QR encoder is ported from (qrcodegen), and the reference shaders that draw the marker as one
  # quad (sdk/shaders; shaders/ in a release archive)
  install(FILES ${MB_FRAMEPACING_LEGAL_DIR}/licenses/qrcodegen-MIT.txt DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacing/licenses)
  install(DIRECTORY ${MB_FRAMEPACING_META_DIR}/shaders/ DESTINATION ${CMAKE_INSTALL_DATADIR}/mb_framepacing/shaders)
endif()

if(MB_FRAMEPACING_BUILD_TOOLS)
  add_executable(marker-render tools/marker-render/main.cpp)
  target_link_libraries(marker-render PRIVATE mb_framepacing::marker)
  target_compile_options(marker-render PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
endif()

# The reference QR encoder (reference/): the vendored qrcodegen as it is, with access to its mask penalty. Built for the tests and the
# benchmarks only; the library does not contain it
if(MB_FRAMEPACING_BUILD_TESTS OR MB_FRAMEPACING_BUILD_BENCHMARKS)
  add_library(mb_framepacing_qrcodegen_reference OBJECT reference/QrcodegenReference.c reference/QrcodegenReference.h)
  set_target_properties(mb_framepacing_qrcodegen_reference PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON)
  target_include_directories(mb_framepacing_qrcodegen_reference PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/reference ${CMAKE_CURRENT_SOURCE_DIR}/reference/third_party/qrcodegen)
endif()

if(MB_FRAMEPACING_BUILD_TESTS)
  add_executable(mb_framepacing_marker_tests tests/FrameMarkerTests.cpp tests/AllocationTests.cpp tests/QrEncoderTests.cpp)
  target_link_libraries(mb_framepacing_marker_tests
    PRIVATE mb_framepacing::marker mb_framepacing_qrcodegen_reference mb_framepacing_test_support GTest::gtest_main)
  # The tests check the private wire format and the QR encoder too (source/mb/framepacing/marker/detail)
  target_include_directories(mb_framepacing_marker_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/source)
  target_compile_options(mb_framepacing_marker_tests PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  mb_framepacing_add_x11_macro_check(mb_framepacing_marker_tests marker)
  gtest_discover_tests(mb_framepacing_marker_tests)

  # The QR encoder against its reference with far more input than the unit tests (tests/stress): a short run with the tests, the long
  # one by hand (no arguments: about two million payloads on every core, and every line of 25 modules)
  find_package(Threads REQUIRED)
  add_executable(mb_framepacing_marker_qr_stress tests/stress/QrEncoderStress.cpp)
  target_link_libraries(mb_framepacing_marker_qr_stress PRIVATE mb_framepacing::marker mb_framepacing_qrcodegen_reference Threads::Threads)
  target_include_directories(mb_framepacing_marker_qr_stress PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/source)
  target_compile_options(mb_framepacing_marker_qr_stress PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
  add_test(NAME QrEncoderStress.ShortRun
    COMMAND mb_framepacing_marker_qr_stress --payloads 20000 --symbols 4000 --lines 2000000 --no-exhaustive)
endif()

if(MB_FRAMEPACING_BUILD_BENCHMARKS)
  add_executable(mb_framepacing_marker_benchmarks
    benchmarks/BitmapBenchmarks.cpp
    benchmarks/DrawBenchmarks.cpp
    benchmarks/EncodeBenchmarks.cpp
    benchmarks/FrameBenchmarks.cpp)
  target_link_libraries(mb_framepacing_marker_benchmarks PRIVATE mb_framepacing::marker mb_framepacing_qrcodegen_reference benchmark::benchmark_main)
  target_compile_options(mb_framepacing_marker_benchmarks PRIVATE ${MB_FRAMEPACING_CXX_WARNINGS})
endif()

if(MB_FRAMEPACING_BUILD_FUZZERS)
  if(NOT CMAKE_CXX_COMPILER_ID MATCHES "Clang")
    message(FATAL_ERROR "MB_FRAMEPACING_BUILD_FUZZERS needs Clang (libFuzzer)")
  endif()
  # The QR encoder against its reference, with inputs libFuzzer chooses (tests/fuzz). The encoder's source is compiled into the fuzzer,
  # so that the fuzzer sees which of its branches an input reaches
  add_executable(mb_framepacing_marker_qr_fuzz
    tests/fuzz/QrEncoderFuzz.cpp
    source/mb/framepacing/marker/detail/QrEncoder.cpp
    reference/QrcodegenReference.c)
  set_target_properties(mb_framepacing_marker_qr_fuzz PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON)
  target_compile_features(mb_framepacing_marker_qr_fuzz PRIVATE cxx_std_20)
  target_link_libraries(mb_framepacing_marker_qr_fuzz PRIVATE mb_framepacing::core)
  target_include_directories(mb_framepacing_marker_qr_fuzz PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/source
    ${CMAKE_CURRENT_SOURCE_DIR}/reference
    ${CMAKE_CURRENT_SOURCE_DIR}/reference/third_party/qrcodegen)
  target_compile_options(mb_framepacing_marker_qr_fuzz PRIVATE -fsanitize=fuzzer,address)
  target_link_options(mb_framepacing_marker_qr_fuzz PRIVATE -fsanitize=fuzzer,address)
endif()
