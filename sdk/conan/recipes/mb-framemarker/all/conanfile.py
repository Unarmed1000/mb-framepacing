# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
"""Conan recipe for mb_framemarker, the C++20 frame marker library: it builds the release archive of the marker-v<version> tag
(conandata.yml has its URL and SHA-256). Laid out as conan-center-index recipes are, so the same folder can go there."""

import os

from conan import ConanFile
from conan.tools.build import check_min_cppstd
from conan.tools.cmake import CMake, CMakeToolchain, cmake_layout
from conan.tools.files import copy, get, rmdir

# 2.2: local-recipes-index remotes (sdk/conan as a remote)
required_conan_version = ">=2.2"


class MbFrameMarkerConan(ConanFile):
    name = "mb-framemarker"
    description = "Draws a QR frame marker into every frame, so a capture card or camera can measure frame pacing and animation error"
    license = "BSD-3-Clause"
    url = "https://github.com/Unarmed1000/mb-framepacing"
    homepage = "https://github.com/Unarmed1000/mb-framepacing"
    topics = ("frame-pacing", "animation-error", "qr-code", "graphics", "benchmark")
    package_type = "static-library"
    settings = "os", "arch", "compiler", "build_type"
    options = {"fPIC": [True, False]}
    default_options = {"fPIC": True}
    implements = ["auto_shared_fpic"]

    def layout(self):
        cmake_layout(self, src_folder="src")

    def validate(self):
        check_min_cppstd(self, 20)

    def build_requirements(self):
        self.tool_requires("cmake/[>=4.0 <5]")

    def source(self):
        get(self, **self.conan_data["sources"][self.version], strip_root=True)

    def generate(self):
        toolchain = CMakeToolchain(self)
        # Conan builds the library as the top level project, where the tests, marker-render and warnings as errors default to on
        toolchain.cache_variables["MB_FRAMEMARKER_BUILD_TESTS"] = False
        toolchain.cache_variables["MB_FRAMEMARKER_BUILD_TOOLS"] = False
        toolchain.cache_variables["MB_FRAMEMARKER_WARNINGS_AS_ERRORS"] = False
        toolchain.generate()

    def build(self):
        cmake = CMake(self)
        cmake.configure()
        cmake.build()

    def package(self):
        licenses = os.path.join(self.package_folder, "licenses")
        copy(self, "LICENSE", self.source_folder, licenses)
        # The one third-party component compiled into the library
        copy(self, "qrcodegen-MIT.txt", os.path.join(self.source_folder, "licenses"), licenses)
        cmake = CMake(self)
        cmake.install()
        # Conan generates the CMake config files; share/mb_framemarker keeps the reference shaders
        rmdir(self, os.path.join(self.package_folder, "lib", "cmake"))

    def package_info(self):
        self.cpp_info.set_property("cmake_file_name", "mb_framemarker")
        self.cpp_info.set_property("cmake_target_name", "mb::framemarker")
        self.cpp_info.libs = ["mb_framemarker"]
