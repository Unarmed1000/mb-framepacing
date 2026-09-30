#ifndef MB_FRAMEPACING_CORE_HPP
#define MB_FRAMEPACING_CORE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB::FramePacing - the core of the mb-framepacing SDK: what every module shares. The library version, the time unit of every time the
// SDK reads or writes (100 ns ticks, C# TimeSpan ticks) with conversions from the units platforms report, Point and Rectangle. The SDK never
// reads a clock: the application passes its own clock's times.
//
// This is the header to include: it pulls in every type (one header per type) and declares the functions. Every module's header
// (Marker.hpp, Data.hpp) includes it.

#include <mb/framepacing/core/LibraryVersion.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/Ticks.hpp>

namespace MB::FramePacing
{
  //! The linked library's version (every module has the same). Version.hpp has it at compile time; this header does not include it, so a
  //! version bump does not rebuild every file that includes the library.
  LibraryVersion GetLibraryVersion() noexcept;
}

#endif
