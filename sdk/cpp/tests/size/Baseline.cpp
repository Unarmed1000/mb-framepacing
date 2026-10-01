// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The size probes' baseline: the same program without the SDK.
#include <cstdio>
#include <span>

int main(int argc, char* argv[])
{
  const std::span<char* const> arguments(argv, static_cast<std::size_t>(argc));
  std::printf("%d %s\n", argc, arguments[0]);
  return 0;
}
