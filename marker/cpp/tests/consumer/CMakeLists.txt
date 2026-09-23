# A minimal project that uses mb_framemarker the ways doc/integrating.md describes. check_consumers.py builds and runs it in CI.
#   MB_CONSUMER_MODE=fetchcontent  FetchContent with URL + URL_HASH (the release archive); FETCHCONTENT_SOURCE_DIR_MB_FRAMEMARKER may
#                                  point at a local source tree instead
#   MB_CONSUMER_MODE=subdirectory  add_subdirectory(MB_FRAMEMARKER_SOURCE_DIR)
#   MB_CONSUMER_MODE=package       find_package after cmake --install (CMAKE_PREFIX_PATH)
cmake_minimum_required(VERSION 4.0)
project(mb_framemarker_consumer LANGUAGES CXX)

set(MB_CONSUMER_MODE "fetchcontent" CACHE STRING "fetchcontent, subdirectory or package")
set(MB_CONSUMER_VERSION "0.1" CACHE STRING "The mb_framemarker version to ask for")
set(MB_FRAMEMARKER_URL "https://github.com/Unarmed1000/mb-framepacing/releases/download/marker-v0.1.0/mb-framemarker-cpp-0.1.0.tar.gz" CACHE STRING "")
set(MB_FRAMEMARKER_SHA256 "0000000000000000000000000000000000000000000000000000000000000000" CACHE STRING "")
set(MB_FRAMEMARKER_SOURCE_DIR "" CACHE PATH "Source tree for the subdirectory mode")

if(MB_CONSUMER_MODE STREQUAL "subdirectory")
  add_subdirectory(${MB_FRAMEMARKER_SOURCE_DIR} mb_framemarker)
elseif(MB_CONSUMER_MODE STREQUAL "package")
  find_package(mb_framemarker ${MB_CONSUMER_VERSION} CONFIG REQUIRED)
else()
  include(FetchContent)
  FetchContent_Declare(mb_framemarker
    URL ${MB_FRAMEMARKER_URL}
    URL_HASH SHA256=${MB_FRAMEMARKER_SHA256}
    FIND_PACKAGE_ARGS ${MB_CONSUMER_VERSION} CONFIG)
  FetchContent_MakeAvailable(mb_framemarker)
endif()

add_executable(consumer main.cpp)
target_link_libraries(consumer PRIVATE mb::framemarker)
