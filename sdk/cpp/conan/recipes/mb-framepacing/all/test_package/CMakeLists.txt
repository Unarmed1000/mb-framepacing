# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
cmake_minimum_required(VERSION 3.15)
project(test_package LANGUAGES CXX)

find_package(mb_framepacing CONFIG REQUIRED COMPONENTS marker data)

add_executable(test_package test_package.cpp)
target_link_libraries(test_package PRIVATE mb_framepacing::marker mb_framepacing::data)
target_compile_features(test_package PRIVATE cxx_std_20)
