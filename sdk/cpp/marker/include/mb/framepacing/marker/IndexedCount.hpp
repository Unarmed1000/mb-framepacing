#ifndef MB_FRAMEPACING_MARKER_INDEXEDCOUNT_HPP
#define MB_FRAMEPACING_MARKER_INDEXEDCOUNT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstddef>

namespace MB::FramePacing::Marker
{
  //! How many vertices and indices an indexed triangle list call wrote.
  struct IndexedCount
  {
    std::size_t VertexCount{0};
    std::size_t IndexCount{0};
  };
}

#endif
