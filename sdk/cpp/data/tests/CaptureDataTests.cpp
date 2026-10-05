// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// captures.mbcd: header fields at their offsets, newer and foreign files refused, records read back, a partial last record ignored.
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/capture/CaptureDataHeader.hpp>
#include <mb/framepacing/data/capture/CaptureDataReader.hpp>
#include <mb/framepacing/data/capture/CaptureDataRecord.hpp>
#include <mb/framepacing/data/capture/CaptureDataStatus.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <gtest/gtest.h>
#include <array>
#include <bit>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>
#include "mb/framepacing/data/capture/detail/CaptureDataFormat.hpp"

namespace FP = MB::FramePacing;
namespace FD = MB::FramePacing::Data;
namespace FM = MB::FramePacing::Marker;

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
    // The sync marker's region, after the marker locations
    Put(bytes, 168, 14, 4);
    Put(bytes, 172, 820, 4);
    Put(bytes, 176, 240, 4);
    Put(bytes, 180, 246, 4);
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
    std::copy(second.begin(), second.end(), bytes.begin() + 144);
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
  EXPECT_EQ(header.SyncRegion, (MB::FramePacing::Rectangle(14, 820, 240, 246)));
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
  for (const auto& record : {RecordBytes(0, 200, 1, main, second), RecordBytes(2, FD::CaptureDataFormat::UnknownTicks, 0, {}, {})})
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
  EXPECT_EQ(records[0].DeviceTime, FP::TickCount64(200));
  EXPECT_EQ(records[0].CaptureStatus, FD::CaptureDataStatus::Decoded);
  EXPECT_EQ(records[0].MainBytes, main);
  EXPECT_EQ(records[0].SecondBytes, second);
  EXPECT_EQ(records[1].SourceDrops, 3u);
  EXPECT_FALSE(records[1].DeviceTime.has_value());
  EXPECT_TRUE(records[1].MainBytes.empty());
}

TEST(CaptureData, AHeaderThatIsNotTheFormatIsRefused)
{
  const auto with = [](const std::size_t offset, const uint64_t value, const std::size_t size)
  {
    auto bytes = HeaderBytes();
    Put(bytes, offset, value, size);
    return bytes;
  };
  EXPECT_EQ(FD::CaptureDataHeader::Parse(HeaderBytes()).Width, 960) << "the bytes these cases change one field of";
  auto shortBytes = HeaderBytes();
  shortBytes.pop_back();
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(shortBytes), FD::DataFormatError) << "a byte short";
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(std::span<const uint8_t>()), FD::DataFormatError) << "nothing";
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(with(0, FD::CaptureDataFormat::Magic + 1u, 4)), FD::DataFormatError) << "another magic";
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(with(4, 0, 2)), FD::DataFormatError) << "version 0";
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(with(6, FD::CaptureDataFormat::HeaderSize - 1u, 2)), FD::DataFormatError) << "another header size";
  EXPECT_THROW((void)FD::CaptureDataHeader::Parse(with(8, FD::CaptureDataFormat::RecordSize - 1u, 4)), FD::DataFormatError) << "another record size";
  const auto none = FD::CaptureDataHeader::Parse(with(12, 0, 4));
  EXPECT_FALSE(none.FramesStored);
  EXPECT_FALSE(none.Camera);
  EXPECT_TRUE(FD::CaptureDataHeader::Parse(with(64, 0, 4)).Markers.empty());
  auto longer = HeaderBytes();
  longer.resize(longer.size() + 8u, 0xEEu);
  EXPECT_EQ(FD::CaptureDataHeader::Parse(longer).Height, 540) << "more bytes than a header";
}

TEST(CaptureData, ARecordThatIsNotOneIsRefused)
{
  const std::vector<uint8_t> main{'m', 'a', 'i', 'n'};
  const std::vector<uint8_t> second{'s', 'e', 'c'};
  const auto good = RecordBytes(7, 200, 2, main, second);
  const auto record = FD::CaptureDataRecord::Parse(good);
  EXPECT_EQ(record.CaptureIndex, 7);
  EXPECT_EQ(record.HostTime, FP::TickCount64(700));
  EXPECT_EQ(record.CaptureStatus, FD::CaptureDataStatus::Torn);
  EXPECT_EQ(record.MainBytes, main);
  EXPECT_EQ(record.SecondBytes, second);

  auto shortBytes = good;
  shortBytes.pop_back();
  EXPECT_THROW((void)FD::CaptureDataRecord::Parse(shortBytes), FD::DataFormatError) << "a byte short";
  const auto with = [&good](const std::size_t offset, const uint8_t value)
  {
    auto bytes = good;
    bytes[offset] = value;
    return bytes;
  };
  EXPECT_THROW((void)FD::CaptureDataRecord::Parse(with(28, 3)), FD::DataFormatError) << "an unknown status";
  EXPECT_EQ(good.size(), 256u);
  EXPECT_THROW((void)FD::CaptureDataRecord::Parse(with(29, 113)), FD::DataFormatError) << "a main marker longer than its slot";
  EXPECT_THROW((void)FD::CaptureDataRecord::Parse(with(30, 113)), FD::DataFormatError) << "a second marker longer than its slot";
  EXPECT_EQ(FD::CaptureDataRecord::Parse(with(29, 112)).MainBytes.size(), 112u);
  EXPECT_EQ(FD::CaptureDataRecord::Parse(with(30, 112)).SecondBytes.size(), 112u);
}

TEST(CaptureData, ARecordsMarkersDecode)
{
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
  FM::StartMetadata start;
  start.UtcTicks = 5;
  const std::size_t mainSize =
    FM::EncodePayload({FM::MarkerKind::SequenceStart, 7u, 12u, FM::MarkerFlags::StaticAfter, FP::TimeSpan(34)}, start, buffer);
  const std::vector<uint8_t> main(buffer.begin(), buffer.begin() + static_cast<std::ptrdiff_t>(mainSize));
  const std::size_t syncSize = FM::EncodePayload({FM::MarkerKind::Sync, 7u, 11u, FM::MarkerFlags::NoFlags, FP::TimeSpan(0)}, {}, buffer);
  const std::vector<uint8_t> sync(buffer.begin(), buffer.begin() + static_cast<std::ptrdiff_t>(syncSize));
  ASSERT_EQ(main.size(), 81u) << "the longest marker";
  ASSERT_EQ(sync.size(), 20u) << "the shortest";

  const auto record = FD::CaptureDataRecord::Parse(RecordBytes(5, 200, 2, main, sync));
  FM::Payload payload;
  FM::StartMetadata metadata;
  ASSERT_TRUE(record.TryDecodeMain(payload, &metadata));
  EXPECT_EQ(payload.Kind(), FM::MarkerKind::SequenceStart);
  EXPECT_EQ(payload.RunId(), 7u);
  EXPECT_EQ(payload.FrameIndex(), 12u);
  EXPECT_EQ(payload.AnimationTime(), FP::TimeSpan(34));
  EXPECT_EQ(metadata.UtcTicks, 5);
  FM::Payload withoutMetadata;
  EXPECT_TRUE(record.TryDecodeMain(withoutMetadata));
  EXPECT_EQ(withoutMetadata.FrameIndex(), 12u);
  FM::Payload second;
  ASSERT_TRUE(record.TryDecodeSecond(second));
  EXPECT_EQ(second.Kind(), FM::MarkerKind::Sync);
  EXPECT_EQ(second.RunId(), 7u);
  EXPECT_EQ(second.FrameIndex(), 11u);

  // A record without markers, and bytes that are no marker
  const auto none = FD::CaptureDataRecord::Parse(RecordBytes(5, 200, 0, {}, {}));
  FM::Payload untouched{FM::MarkerKind::Frame, 99u, 98u, FM::MarkerFlags::NoFlags, FP::TimeSpan(97)};
  EXPECT_FALSE(none.TryDecodeMain(untouched));
  EXPECT_FALSE(none.TryDecodeSecond(untouched));
  const auto garbage = FD::CaptureDataRecord::Parse(RecordBytes(5, 200, 1, std::vector<uint8_t>(57), {1, 2, 3}));
  EXPECT_FALSE(garbage.TryDecodeMain(untouched));
  EXPECT_FALSE(garbage.TryDecodeSecond(untouched));
}

TEST(CaptureData, WhatIsNoCaptureDataFileIsRefused)
{
  const auto path = std::filesystem::temp_directory_path() / "mb_framepacing_data_test-refused.mbcd";
  const auto write = [&path](const std::vector<uint8_t>& bytes)
  {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    out.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
  };
  auto shortFile = HeaderBytes();
  shortFile.pop_back();
  write(shortFile);
  EXPECT_THROW(FD::CaptureDataReader{path}, FD::DataFormatError) << "ends in its header";
  write({});
  EXPECT_THROW(FD::CaptureDataReader{path}, FD::DataFormatError) << "empty";
  write(std::vector<uint8_t>(FD::CaptureDataFormat::HeaderSize));
  EXPECT_THROW(FD::CaptureDataReader{path}, FD::DataFormatError) << "another file";

  // A header and no records, and a record by an index the file does not have
  auto file = HeaderBytes();
  write(file);
  {
    FD::CaptureDataReader reader(path);
    EXPECT_EQ(reader.RecordCount(), 0);
    EXPECT_TRUE(reader.ReadAll().empty());
    EXPECT_THROW((void)reader.ReadRecord(0), std::out_of_range);
    EXPECT_THROW((void)reader.ReadRecord(-1), std::out_of_range);
    EXPECT_EQ(reader.Header().Width, 960);
    EXPECT_EQ(FD::CaptureDataReader::FileName, "captures.mbcd");
  }
  const auto record = RecordBytes(3, 200, 0, {}, {});
  file.insert(file.end(), record.begin(), record.end());
  write(file);
  {
    FD::CaptureDataReader reader(path);
    EXPECT_EQ(reader.ReadRecord(0).CaptureIndex, 3);
    EXPECT_THROW((void)reader.ReadRecord(1), std::out_of_range);
    // A record's content error is a format error, not an argument error
    file[FD::CaptureDataFormat::HeaderSize + 28u] = 9;
  }
  write(file);
  {
    FD::CaptureDataReader reader(path);
    EXPECT_THROW((void)reader.ReadRecord(0), FD::DataFormatError);
    EXPECT_THROW((void)reader.ReadAll(), FD::DataFormatError);
  }
  std::filesystem::remove(path);

  // A file that is not there is not a format error
  try
  {
    const FD::CaptureDataReader reader(path);
    FAIL() << "a missing file was read";
  }
  catch (const FD::DataFormatError&)
  {
    FAIL() << "a file that cannot be opened is not a format error";
  }
  catch (const std::runtime_error& error)
  {
    EXPECT_NE(std::string(error.what()).find("refused.mbcd"), std::string::npos);
  }
}
