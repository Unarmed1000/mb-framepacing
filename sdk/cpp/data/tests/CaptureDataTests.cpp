// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// captures.mbcd: header fields at their offsets, newer and foreign files refused, records read back, a partial last record ignored.
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/capture/CaptureDataHeader.hpp>
#include <mb/framepacing/data/capture/CaptureDataReader.hpp>
#include <mb/framepacing/data/capture/CaptureDataRecord.hpp>
#include <mb/framepacing/data/capture/CaptureDataStatus.hpp>
#include <gtest/gtest.h>
#include <array>
#include <bit>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include "mb/framepacing/data/capture/detail/CaptureDataFormat.hpp"

namespace FD = MB::FramePacing::Data;

namespace
{
  void Put(std::vector<uint8_t>& bytes, const std::size_t offset, const uint64_t value, const std::size_t size)
  {
    for (std::size_t i = 0; i < size; ++i)
    {
      bytes[offset + i] = static_cast<uint8_t>((value >> (8u * i)) & 0xFFu);
    }
  }

  std::vector<uint8_t> HeaderBytes(const uint16_t version = FD::CaptureDataFormat::Version, const uint32_t markers = 1)
  {
    std::vector<uint8_t> bytes(FD::CaptureDataFormat::HeaderSize);
    Put(bytes, 0, FD::CaptureDataFormat::Magic, 4);
    Put(bytes, 4, version, 2);
    Put(bytes, 6, FD::CaptureDataFormat::HeaderSize, 2);
    Put(bytes, 8, FD::CaptureDataFormat::RecordSize, 4);
    Put(bytes, 12, 3, 4);
    Put(bytes, 16, 960, 4);
    Put(bytes, 20, 540, 4);
    Put(bytes, 24, 60000, 4);
    Put(bytes, 28, 1001, 4);
    Put(bytes, 32, 1920, 4);
    Put(bytes, 36, 1080, 4);
    Put(bytes, 40, 8, 4);
    Put(bytes, 44, 16, 4);
    Put(bytes, 48, 960, 4);
    Put(bytes, 52, 540, 4);
    Put(bytes, 64, markers, 4);
    for (uint32_t i = 0; i < markers; ++i)
    {
      const std::size_t slot = 72 + (24 * static_cast<std::size_t>(i));
      Put(bytes, slot, 40, 4);
      Put(bytes, slot + 4, 32u + i, 4);
      Put(bytes, slot + 8, 147, 4);
      Put(bytes, slot + 12, 147, 4);
      Put(bytes, slot + 16, std::bit_cast<uint64_t>(3.0), 8);
    }
    return bytes;
  }

  std::vector<uint8_t> RecordBytes(const int64_t index, const int64_t device, const uint8_t status, const std::vector<uint8_t>& main,
                                   const std::vector<uint8_t>& second)
  {
    std::vector<uint8_t> bytes(FD::CaptureDataFormat::RecordSize);
    Put(bytes, 0, static_cast<uint64_t>(index), 8);
    Put(bytes, 8, static_cast<uint64_t>(index * 100), 8);
    Put(bytes, 16, static_cast<uint64_t>(device), 8);
    Put(bytes, 24, index == 2 ? 3u : 0u, 4);
    bytes[28] = status;
    bytes[29] = static_cast<uint8_t>(main.size());
    bytes[30] = static_cast<uint8_t>(second.size());
    std::copy(main.begin(), main.end(), bytes.begin() + 32);
    std::copy(second.begin(), second.end(), bytes.begin() + 112);
    return bytes;
  }
}

TEST(CaptureData, TheHeaderFieldsAreWhereTheFormatSays)
{
  const auto header = FD::CaptureDataHeader::Parse(HeaderBytes(FD::CaptureDataFormat::Version, 2));
  EXPECT_EQ(header.Width, 960);
  EXPECT_EQ(header.Height, 540);
  EXPECT_EQ(header.FrameRateNumerator, 60000u);
  EXPECT_EQ(header.FrameRateDenominator, 1001u);
  EXPECT_EQ(header.SourceWidth, 1920);
  EXPECT_EQ(header.SourceHeight, 1080);
  EXPECT_EQ(header.Region, (MB::FramePacing::Rectangle(8, 16, 960, 540)));
  ASSERT_EQ(header.Markers.size(), 2u);
  EXPECT_EQ(header.Markers[1].Bounds.Y(), 33);
  EXPECT_EQ(header.Markers[1].ModuleSizePx, 3.0);
  EXPECT_TRUE(header.FramesStored);
  EXPECT_TRUE(header.Camera);
}

TEST(CaptureData, NewerAndForeignFilesAreRefused)
{
  try
  {
    (void)FD::CaptureDataHeader::Parse(HeaderBytes(FD::CaptureDataFormat::Version + 1));
    FAIL() << "a newer format was read";
  }
  catch (const FD::DataFormatError& error)
  {
    EXPECT_NE(std::string(error.what()).find("update"), std::string::npos);
  }
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(std::vector<uint8_t>(FD::CaptureDataFormat::HeaderSize)), FD::DataFormatError);
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(HeaderBytes(FD::CaptureDataFormat::Version, 5)), FD::DataFormatError);
}

TEST(CaptureData, RecordsReadBackAndAPartialLastRecordIsIgnored)
{
  std::vector<uint8_t> main(FD::CaptureDataFormat::MainMarkerCapacity);
  for (std::size_t i = 0; i < main.size(); ++i)
  {
    main[i] = static_cast<uint8_t>(i);
  }
  const std::vector<uint8_t> second(FD::CaptureDataFormat::SecondMarkerCapacity, 0x5Au);
  auto file = HeaderBytes();
  for (const auto& record : {RecordBytes(0, 200, 1, main, second), RecordBytes(2, FD::CaptureDataRecord::UnknownTicks, 0, {}, {})})
  {
    file.insert(file.end(), record.begin(), record.end());
  }
  file.resize(file.size() + (FD::CaptureDataFormat::RecordSize / 2));

  const auto path = std::filesystem::temp_directory_path() / "mb_framepacing_data_test.mbcd";
  {
    std::ofstream out(path, std::ios::binary);
    out.write(reinterpret_cast<const char*>(file.data()), static_cast<std::streamsize>(file.size()));
  }
  std::vector<FD::CaptureDataRecord> records;
  {
    FD::CaptureDataReader reader(path);
    EXPECT_EQ(reader.RecordCount(), 2);
    records = reader.ReadAll();
    EXPECT_EQ(reader.ReadRecord(1), records[1]);
  }
  std::filesystem::remove(path);

  EXPECT_EQ(records[0].CaptureIndex, 0);
  EXPECT_EQ(records[0].DeviceTicks, 200);
  EXPECT_EQ(records[0].Status, FD::CaptureDataStatus::Decoded);
  EXPECT_EQ(records[0].MainBytes, main);
  EXPECT_EQ(records[0].SecondBytes, second);
  EXPECT_EQ(records[1].SourceDrops, 3u);
  EXPECT_FALSE(records[1].HasDeviceTicks());
  EXPECT_TRUE(records[1].MainBytes.empty());
}
