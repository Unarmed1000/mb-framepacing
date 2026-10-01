// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Size probe: the core's time types and ByteSpanUtil, as an application converts its clock and writes a value.
#include <mb/framepacing/core/ByteSpanUtil.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <array>
#include <cstdint>
#include <cstdio>
#include <span>

namespace FP = MB::FramePacing;

int main(int argc, char* argv[])
{
  const std::span<char* const> arguments(argv, static_cast<std::size_t>(argc));
  const FP::TickCount64 now = FP::TickCount64::FromCounter(int64_t{argc} * 1'000'003, 10'000'000);
  const FP::TimeSpan step = FP::TimeSpan::FromSeconds(1.0 / (argc + 59));
  std::array<uint8_t, 8> bytes{};
  FP::ByteSpanUtil::WriteLE(bytes, 0, (now + step).Ticks());
  std::printf("%lld %s\n", static_cast<long long>(FP::ByteSpanUtil::ReadLE<int64_t>(bytes, 0)), arguments[0]);
  return 0;
}
