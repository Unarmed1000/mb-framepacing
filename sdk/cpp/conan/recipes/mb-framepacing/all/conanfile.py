# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
"""Conan recipe for mb_framepacing, the C++20 library of the mb-framepacing SDK: it builds the C++ release archive of the sdk-v<version>
tag (conandata.yml has its URL and SHA-256). Every module is a component (mb_framepacing::core, ::marker, ::data, ::pacer); with_marker,
with_data leaves a module out; with_pacer (off by default: the pacer is experimental, sdk/doc/pacer.md) adds one. Laid out as conan-center-index
recipes are, so the same folder can go there."""

import os

from conan import ConanFile
from conan.errors import ConanInvalidConfiguration
from conan.tools.build import check_min_cppstd
from conan.tools.cmake import CMake, CMakeDeps, CMakeToolchain, cmake_layout
from conan.tools.files import copy, get, rmdir

# 2.2: local-recipes-index remotes (sdk/cpp/conan as a remote)
required_conan_version = ">=2.2"


class MbFramePacingConan(ConanFile):
    name = "mb-framepacing"
    description = (
        "The mb-framepacing SDK: draws a QR frame marker into every frame, so a capture card or camera can measure frame pacing and animation "
        "error, reads the tools' capture data and analysis output, and has an experimental frame pacer (with_pacer, off by default)"
    )
    license = "BSD-3-Clause"
    url = "https://github.com/Unarmed1000/mb-framepacing"
    homepage = "https://github.com/Unarmed1000/mb-framepacing"
    topics = ("frame-pacing", "animation-error", "qr-code", "graphics", "benchmark", "capture")
    package_type = "static-library"
    settings = "os", "arch", "compiler", "build_type"
    options = {"fPIC": [True, False], "with_marker": [True, False], "with_data": [True, False], "with_pacer": [True, False]}
    default_options = {"fPIC": True, "with_marker": True, "with_data": True, "with_pacer": False}
    implements = ["auto_shared_fpic"]

    def layout(self):
        cmake_layout(self, src_folder="src")

    def requirements(self):
        if self.options.with_data:
            # summary.json, inside the data module only (header only, not part of the API)
            self.requires("nlohmann_json/3.12.0", visible=False)

    def validate(self):
        check_min_cppstd(self, 20)
        if self.options.with_data and not self.options.with_marker:
            raise ConanInvalidConfiguration("the data module needs the marker module: with_data=True needs with_marker=True")

    def build_requirements(self):
        self.tool_requires("cmake/[>=4.0 <5]")

    def source(self):
        get(self, **self.conan_data["sources"][self.version], strip_root=True)

    def generate(self):
        toolchain = CMakeToolchain(self)
        # Conan builds the library as the top level project, where the tests, the tools and warnings as errors default to on
        toolchain.cache_variables["MB_FRAMEPACING_BUILD_TESTS"] = False
        toolchain.cache_variables["MB_FRAMEPACING_BUILD_TOOLS"] = False
        toolchain.cache_variables["MB_FRAMEPACING_WARNINGS_AS_ERRORS"] = False
        toolchain.cache_variables["MB_FRAMEPACING_BUILD_MARKER"] = bool(self.options.with_marker)
        toolchain.cache_variables["MB_FRAMEPACING_BUILD_DATA"] = bool(self.options.with_data)
        toolchain.cache_variables["MB_FRAMEPACING_BUILD_PACER"] = bool(self.options.with_pacer)
        # nlohmann/json comes from Conan: FetchContent must find it, never download it
        if self.options.with_data:
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
        # The third-party components compiled into the modules
        if self.options.with_marker:
            copy(self, "qrcodegen-MIT.txt", os.path.join(self.source_folder, "licenses"), licenses)
        if self.options.with_data:
            copy(self, "nlohmann-json-MIT.txt", os.path.join(self.source_folder, "licenses"), licenses)
        cmake = CMake(self)
        cmake.install()
        # Conan generates the CMake config files; share/mb_framepacing keeps the reference shaders
        rmdir(self, os.path.join(self.package_folder, "lib", "cmake"))

    def package_info(self):
        self.cpp_info.set_property("cmake_file_name", "mb_framepacing")
        core = self.cpp_info.components["core"]
        core.set_property("cmake_target_name", "mb_framepacing::core")
        core.libs = ["mb_framepacing_core"]
        if self.options.with_marker:
            marker = self.cpp_info.components["marker"]
            marker.set_property("cmake_target_name", "mb_framepacing::marker")
            marker.libs = ["mb_framepacing_marker"]
            marker.requires = ["core"]
        if self.options.with_data:
            data = self.cpp_info.components["data"]
            data.set_property("cmake_target_name", "mb_framepacing::data")
            data.libs = ["mb_framepacing_data"]
            # nlohmann/json is compiled into the module (visible=False): consumers never see it
            data.requires = ["marker"]
        if self.options.with_pacer:
            pacer = self.cpp_info.components["pacer"]
            pacer.set_property("cmake_target_name", "mb_framepacing::pacer")
            pacer.libs = ["mb_framepacing_pacer"]
            pacer.requires = ["core"]
