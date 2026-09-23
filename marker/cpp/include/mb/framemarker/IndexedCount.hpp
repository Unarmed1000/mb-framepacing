#ifndef MB_FRAMEMARKER_INDEXEDCOUNT_HPP
#define MB_FRAMEMARKER_INDEXEDCOUNT_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstddef>

namespace MB::FrameMarker
{
  //! How many vertices and indices an indexed triangle list call wrote.
  struct IndexedCount
  {
    std::size_t VertexCount{0};
    std::size_t IndexCount{0};
  };
}

#endif
