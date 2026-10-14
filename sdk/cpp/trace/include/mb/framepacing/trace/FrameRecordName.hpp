#ifndef MB_FRAMEPACING_TRACE_FRAMERECORDNAME_HPP
#define MB_FRAMEPACING_TRACE_FRAMERECORDNAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/trace/FrameRecordText.hpp>
#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace MB::FramePacing::Trace
{
  //! The name of a frame record's slice: "mbfp frame <run id>
  //! <frame index>", the two ids of the frame's marker as decimal numbers without leading zeros. The text is held inline and ends
  //! with a zero, so making a name allocates nothing and CStr() can be given to a function that takes a C string. A name made
  //! without ids is empty: the name of no record.
  class FrameRecordName
  {
  public:
    //! The longest name, in characters: that of the largest run id and frame index, "mbfp frame 4294967295 18446744073709551615".
    static constexpr std::size_t MaxLength = 42;

    //! An empty name.
    constexpr FrameRecordName() noexcept = default;

    //! The name of the frame with this run id and frame index.
    constexpr FrameRecordName(const uint32_t runId, const uint64_t frameIndex) noexcept
    {
      for (const char character : FrameRecordText::NamePrefix)
      {
        Append(character);
      }
      AppendDecimal(runId);
      Append(' ');
      AppendDecimal(frameIndex);
    }

    //! The name's characters, without the zero that ends them.
    [[nodiscard]] constexpr std::string_view View() const noexcept
    {
      return {m_text.data(), m_length};
    }

    //! The name as a C string: its characters and a zero.
    [[nodiscard]] constexpr const char* CStr() const noexcept
    {
      return m_text.data();
    }

    //! Two names are equal when their texts are.
    constexpr bool operator==(const FrameRecordName& other) const noexcept
    {
      return View() == other.View();
    }

  private:
    constexpr void Append(const char character) noexcept
    {
      m_text[m_length] = character;
      ++m_length;
    }

    //! The digits come out of a number last one first, so they are counted and then written from the number's end.
    constexpr void AppendDecimal(uint64_t value) noexcept
    {
      std::size_t digitCount = 1;
      for (uint64_t rest = value / 10u; rest != 0u; rest /= 10u)
      {
        ++digitCount;
      }
      for (std::size_t index = digitCount; index > 0u; --index)
      {
        m_text[m_length + index - 1u] = static_cast<char>('0' + static_cast<int>(value % 10u));
        value /= 10u;
      }
      m_length += digitCount;
    }

    //! The characters, then zeros: one more than the longest name, so that a zero always ends it.
    std::array<char, MaxLength + 1> m_text{};
    std::size_t m_length{0};
  };
}

#endif
