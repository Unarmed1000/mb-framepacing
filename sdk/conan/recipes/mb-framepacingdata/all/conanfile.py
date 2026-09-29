# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
"""Conan recipe for mb_framepacingdata, the C++20 reader of the mb-framepacing tools' data (captures.mbcd and the analysis output): it
builds the release archive of the data-v<version> tag (conandata.yml has its URL and SHA-256). Laid out as conan-center-index recipes
are, so the same folder can go there."""

import os

from conan import ConanFile
from conan.tools.build import check_min_cppstd
from conan.tools.cmake import CMake, CMakeDeps, CMakeToolchain, cmake_layout
from conan.tools.files import copy, get, rmdir

# 2.2: local-recipes-index remotes (sdk/conan as a remote)
required_conan_version = ">=2.2"


class MbFramePacingDataConan(ConanFile):
    name = "mb-framepacingdata"
    description = "Reads the mb-framepacing tools' capture data (captures.mbcd) and analysis output (summary.json and the CSV files)"
    license = "BSD-3-Clause"
    url = "https://github.com/Unarmed1000/mb-framepacing"
    homepage = "https://github.com/Unarmed1000/mb-framepacing"
    topics = ("frame-pacing", "animation-error", "capture", "graphics", "benchmark")
    package_type = "static-library"
    settings = "os", "arch", "compiler", "build_type"
    options = {"fPIC": [True, False]}
    default_options = {"fPIC": True}
    implements = ["auto_shared_fpic"]

    def layout(self):
        cmake_layout(self, src_folder="src")

    def requirements(self):
        # The public headers use the marker library's types (MB_FRAMEPACINGDATA_MARKER_VERSION in CMakeLists.txt: 0.x, the same minor)
        self.requires("mb-framemarker/[>=0.1 <0.2]", transitive_headers=True)
        # summary.json, inside the library only (header only, not part of the API)
        self.requires("nlohmann_json/3.12.0", visible=False)

    def validate(self):
        check_min_cppstd(self, 20)

    def build_requirements(self):
        self.tool_requires("cmake/[>=4.0 <5]")

    def source(self):
        get(self, **self.conan_data["sources"][self.version], strip_root=True)

    def generate(self):
        toolchain = CMakeToolchain(self)
        # Conan builds the library as the top level project, where the tests and warnings as errors default to on
        toolchain.cache_variables["MB_FRAMEPACINGDATA_BUILD_TESTS"] = False
        toolchain.cache_variables["MB_FRAMEPACINGDATA_WARNINGS_AS_ERRORS"] = False
        # nlohmann/json comes from Conan: FetchContent must find it, never download it
        toolchain.cache_variables["CMAKE_REQUIRE_FIND_PACKAGE_nlohmann_json"] = True
        toolchain.generate()
        CMakeDeps(self).generate()

    def build(self):
        cmake = CMake(self)
        cmake.configure()
        cmake.build()

    def package(self):
        licenses = os.path.join(self.package_folder, "licenses")
        copy(self, "LICENSE", self.source_folder, licenses)
        # The one third-party component compiled into the library
        copy(self, "nlohmann-json-MIT.txt", os.path.join(self.source_folder, "licenses"), licenses)
        cmake = CMake(self)
        cmake.install()
        # Conan generates the CMake config files
        rmdir(self, os.path.join(self.package_folder, "lib", "cmake"))

    def package_info(self):
        self.cpp_info.set_property("cmake_file_name", "mb_framepacingdata")
        self.cpp_info.set_property("cmake_target_name", "mb::framepacingdata")
        self.cpp_info.libs = ["mb_framepacingdata"]
