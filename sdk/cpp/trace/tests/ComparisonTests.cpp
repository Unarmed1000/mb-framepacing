// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The trace module's comparisons: operator== and operator!= are written by hand (C++17 has no defaulted ones), so every field is
// checked to take part.
#include <mb/framepacing/testing/EqualityCheck.hpp>
#include <mb/framepacing/trace/FrameDetail.hpp>
#include <mb/framepacing/trace/FrameDetailName.hpp>
#include <mb/framepacing/trace/FrameRecordAnnotation.hpp>
#include <mb/framepacing/trace/FrameRecordName.hpp>
#include <mb/framepacing/trace/FrameRecordText.hpp>
#include <gtest/gtest.h>

namespace TR = MB::FramePacing::Trace;
namespace EC = MB::FramePacing::Testing::EqualityCheck;

TEST(Comparison, AnAnnotationComparesItsNameAndValue)
{
  const TR::FrameRecordAnnotation annotation{TR::FrameRecordText::AnimationNs, 5};
  EXPECT_EQ(EC::FirstChangeNotSeen(
              annotation, [](TR::FrameRecordAnnotation& changed) { changed.Name = TR::FrameRecordText::MarkerTargetNs; },
              [](TR::FrameRecordAnnotation& changed) { changed.Value = 6; }),
            -1);
}

TEST(Comparison, TheNamesCompareTheirText)
{
  EXPECT_EQ(EC::FirstChangeNotSeen(TR::FrameRecordName(1u, 2u), [](TR::FrameRecordName& changed) { changed = TR::FrameRecordName(1u, 3u); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(TR::FrameDetailName(TR::FrameDetail::Wait, 1u, 2u),
                                   [](TR::FrameDetailName& changed) { changed = TR::FrameDetailName(TR::FrameDetail::Gpu, 1u, 2u); }),
            -1);
}
