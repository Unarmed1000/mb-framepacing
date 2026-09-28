#ifndef MB_FRAMEPACINGDATA_VALUESTATISTICS_HPP
#define MB_FRAMEPACINGDATA_VALUESTATISTICS_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacingData
{
  //! The statistics of one quantity in summary.json, in milliseconds: percentiles by linear interpolation between the closest ranks, the
  //! sample standard deviation. Count 0 = no value (every other field is then 0).
  struct ValueStatistics
  {
    int64_t Count{0};
    double Min{0.0};
    double Mean{0.0};
    double StdDev{0.0};
    double P50{0.0};
    double P95{0.0};
    double P99{0.0};
    double P999{0.0};
    double Max{0.0};

    bool operator==(const ValueStatistics&) const = default;
  };
}

#endif
