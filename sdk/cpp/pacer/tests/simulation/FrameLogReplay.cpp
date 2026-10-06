// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "FrameLogReplay.hpp"
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <algorithm>
#include <charconv>
#include <cstddef>
#include <cstdint>
#include <fstream>
#include <iterator>
#include <map>
#include <sstream>
#include <stdexcept>
#include <system_error>

namespace MB::FramePacing::Pacer::Simulation
{
  namespace
  {
    //! A line's cells, the empty ones kept
    std::vector<std::string_view> Cells(const std::string_view line)
    {
      std::vector<std::string_view> cells;
      std::size_t first = 0;
      for (;;)
      {
        const std::size_t comma = line.find(',', first);
        if (comma == std::string_view::npos)
        {
          cells.push_back(line.substr(first));
          return cells;
        }
        cells.push_back(line.substr(first, comma - first));
        first = comma + 1;
      }
    }

    //! A cell that is a whole number, 0 for any other
    int64_t Whole(const std::string_view cell) noexcept
    {
      int64_t value = 0;
      const auto parsed = std::from_chars(cell.data(), cell.data() + cell.size(), value);
      return parsed.ec == std::errc() && parsed.ptr == cell.data() + cell.size() ? value : 0;
    }

    //! The columns of a frame log by name: a row's value, 0 where the log has no such column
    class Columns
    {
      std::map<std::string_view, std::size_t, std::less<>> m_index;

    public:
      explicit Columns(const std::vector<std::string_view>& header)
      {
        for (std::size_t index = 0; index < header.size(); ++index)
        {
          m_index.emplace(header[index], index);
        }
      }

      [[nodiscard]] bool Has(const std::string_view name) const
      {
        return m_index.contains(name);
      }

      [[nodiscard]] int64_t At(const std::vector<std::string_view>& row, const std::string_view name) const
      {
        const auto found = m_index.find(name);
        return found != m_index.end() && found->second < row.size() ? Whole(row[found->second]) : 0;
      }
    };

    //! A span in whole refreshes, rounded, with its sign
    int64_t Refreshes(const RefreshPeriod period, const int64_t ticks) noexcept
    {
      return ticks >= 0 ? period.NearestRefreshes(TimeSpan(ticks)) : -period.NearestRefreshes(TimeSpan(-ticks));
    }
  }

  std::vector<LoggedFrame> ReadFrameLog(const std::string_view text)
  {
    std::vector<std::string_view> lines;
    for (std::size_t first = 0; first < text.size();)
    {
      std::size_t end = text.find('\n', first);
      end = end == std::string_view::npos ? text.size() : end;
      std::string_view line = text.substr(first, end - first);
      if (!line.empty() && line.back() == '\r')
      {
        line.remove_suffix(1);
      }
      if (!line.empty())
      {
        lines.push_back(line);
      }
      first = end + 1;
    }
    if (!lines.empty() && lines.front().starts_with("\xEF\xBB\xBF"))
    {
      lines.front().remove_prefix(3);
    }
    if (lines.empty())
    {
      throw std::runtime_error("not a frame log: no header line");
    }
    const Columns columns(Cells(lines.front()));
    if (!columns.Has("frameIndex") || !columns.Has("frameStartTicks"))
    {
      throw std::runtime_error("not a frame log: no frameIndex and frameStartTicks columns");
    }

    std::vector<LoggedFrame> frames;
    std::vector<int64_t> idsAhead;
    for (std::size_t index = 1; index < lines.size(); ++index)
    {
      const std::vector<std::string_view> row = Cells(lines[index]);
      LoggedFrame frame;
      frame.FrameIndex = columns.At(row, "frameIndex");
      // A log without the column is of a run with the pacer on
      frame.PacerOn = !columns.Has("pacerOn") || columns.At(row, "pacerOn") != 0;
      frame.StartTicks = columns.At(row, "frameStartTicks");
      frame.EndFrameTicks = columns.At(row, "endFrameTicks");
      frame.WorkTicks = columns.At(row, "workCpuTicks") + columns.At(row, "workGpuTicks");
      frame.PresentTicks = columns.At(row, "presentCallTicks");
      frame.ShownTicks = columns.At(row, "firstPixelOutTicks");
      if (frame.ShownTicks == 0)
      {
        frame.ShownTicks = columns.At(row, "feedbackDisplayTicks");
      }
      frame.SwapInterval = static_cast<uint32_t>(std::max<int64_t>(columns.At(row, "swapInterval"), 0));
      frame.AnimationStepTicks = columns.At(row, "animationStepTicks");
      frame.IntendedDisplayTicks = columns.At(row, "intendedDisplayTicks");
      frame.NextFrameStartTicks = columns.At(row, "nextFrameStartTicks");
      frame.TargetFrameTimeTicks = columns.At(row, "targetFrameTimeTicks");
      if (const int64_t frameId = columns.At(row, "pacerFrameId"); frameId != 0)
      {
        idsAhead.push_back(frameId - frame.FrameIndex);
      }
      frames.push_back(frame);
    }
    if (!idsAhead.empty())
    {
      const auto middle = idsAhead.begin() + static_cast<std::ptrdiff_t>(idsAhead.size() / 2);
      std::nth_element(idsAhead.begin(), middle, idsAhead.end());
      if (*middle == 2)
      {
        throw std::runtime_error("a frame log with a frame's start one row early (an OpenGL ES log of the sample before its fix)");
      }
    }
    return frames;
  }

  std::vector<LoggedFrame> ReadFrameLogFile(const std::filesystem::path& path)
  {
    std::ifstream file(path, std::ios::binary);
    if (!file)
    {
      throw std::runtime_error("Cannot open " + path.string());
    }
    const std::string text((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    return ReadFrameLog(std::string_view(text));
  }

  RefreshPeriod LoggedRefreshPeriod(const std::vector<LoggedFrame>& frames)
  {
    std::map<int64_t, int64_t> counts;
    for (const LoggedFrame& frame : frames)
    {
      if (frame.TargetFrameTimeTicks > 0 && frame.SwapInterval > 0)
      {
        ++counts[frame.TargetFrameTimeTicks / static_cast<int64_t>(frame.SwapInterval)];
      }
    }
    if (counts.empty())
    {
      throw std::runtime_error("the frame log has no target frame time to take its refresh period from");
    }
    const auto most = std::max_element(counts.begin(), counts.end(), [](const auto& left, const auto& right) { return left.second < right.second; });
    return RefreshPeriod::FromTimeSpan(TimeSpan(most->first));
  }

  ReplayResult ReplayLog(const std::vector<LoggedFrame>& frames, const RefreshPeriod period, const bool autoSwapInterval)
  {
    PacerSettings settings(period);
    settings.SetAutoSwapInterval(autoSwapInterval);
    FramePacer pacer(settings);

    ReplayResult result;
    std::ostringstream out;
    out << ReplayHeader << '\n';
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      const LoggedFrame& frame = frames[index];
      if (!frame.PacerOn || frame.StartTicks == 0)
      {
        continue;
      }
      const FrameSchedule schedule = pacer.BeginFrame(TickCount64(frame.StartTicks));
      if (frame.EndFrameTicks != 0)
      {
        static_cast<void>(pacer.EndFrame(TickCount64(frame.EndFrameTicks), TimeSpan(frame.WorkTicks)));
      }
      ++result.Frames;

      const int64_t nextFrameStartTicks = schedule.NextFrameStartTime.Ticks();
      if (frame.SwapInterval != 0)
      {
        ++result.Compared;
        if (schedule.SwapInterval == frame.SwapInterval && schedule.AnimationStep.Ticks() == frame.AnimationStepTicks &&
            (frame.NextFrameStartTicks == 0 || nextFrameStartTicks == frame.NextFrameStartTicks))
        {
          ++result.Agreeing;
        }
      }

      // What the display had not shown yet when the frame started: the frames presented before it with a later display time
      int64_t pending = 0;
      for (std::size_t earlier = index; earlier > 0 && index - earlier < 64; --earlier)
      {
        const LoggedFrame& before = frames[earlier - 1];
        if (before.PresentTicks != 0 && before.PresentTicks <= frame.StartTicks && before.ShownTicks > frame.StartTicks)
        {
          ++pending;
        }
      }
      ++result.PendingAtStart[pending];

      const int64_t intendedTicks = schedule.IntendedDisplayTime.Ticks();
      out << frame.FrameIndex << ',' << frame.StartTicks << ',' << schedule.SwapInterval << ',' << frame.SwapInterval << ','
          << schedule.AnimationStep.Ticks() << ',' << frame.AnimationStepTicks << ',' << nextFrameStartTicks << ',' << frame.NextFrameStartTicks
          << ',' << intendedTicks << ',';
      if (frame.ShownTicks != 0)
      {
        ++result.Shown;
        const int64_t toDisplay = Refreshes(period, frame.ShownTicks - frame.StartTicks);
        ++result.RefreshesToDisplay[toDisplay];
        out << frame.ShownTicks << ',';
        if (intendedTicks != 0)
        {
          ++result.RefreshesAfterIntended[Refreshes(period, frame.ShownTicks - intendedTicks)];
          out << (frame.ShownTicks - intendedTicks);
        }
        out << ',' << toDisplay;
      }
      else
      {
        out << ",,";
      }
      out << ',' << pending << '\n';
    }
    result.Csv = out.str();
    return result;
  }
}
