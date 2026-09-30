#ifndef MB_FRAMEPACING_PACER_HPP
#define MB_FRAMEPACING_PACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB::FramePacing::Pacer - the pacer module: paces frames on a grid of refreshes with an adaptive swap interval (Swappy's rule, and a fix
// that slows down sooner), and aligns the animation timer to the refreshes (AnimationClock). See sdk/doc/pacer.md.
//
// Values in, values out: the application passes what the platform knows (FrameInput, FrameEnd) and applies the plan (FrameSchedule);
// the pacer calls no platform API. Nothing allocates per frame.
//
// This is the header to include: it pulls in every type (one header per type) and the core (Core.hpp: the tick units, the steady clock).

#include <mb/framepacing/Core.hpp>
#include <mb/framepacing/pacer/AnimationClock.hpp>
#include <mb/framepacing/pacer/AnimationTime.hpp>
#include <mb/framepacing/pacer/FrameEnd.hpp>
#include <mb/framepacing/pacer/FrameInput.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/FrameSchedule.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/SlowDownRule.hpp>
#include <mb/framepacing/pacer/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/SwapIntervalRule.hpp>
#include <mb/framepacing/pacer/WindowState.hpp>

#endif
