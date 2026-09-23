//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Binary PGM (P5, 8 bit) reader/writer. The C++ marker-render tool writes the golden images in this format and the tools use it for debug
//* dumps, so no image library dependency is needed.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Text;

namespace MB.FramePacing.Marker
{
  public static class PgmFile
  {
    public static GrayImage Read(string path)
    {
      var bytes = File.ReadAllBytes(path);
      int offset = 0;
      if (ReadToken(bytes, ref offset) != "P5")
        throw new InvalidDataException($"'{path}' is not a binary PGM (P5) file");
      int width = int.Parse(ReadToken(bytes, ref offset));
      int height = int.Parse(ReadToken(bytes, ref offset));
      int maxValue = int.Parse(ReadToken(bytes, ref offset));
      if (maxValue != 255)
        throw new InvalidDataException($"'{path}': only 8 bit PGM files are supported (maxval {maxValue})");
      // Exactly one whitespace byte separates the header from the pixel data
      ++offset;
      long pixelCount = (long)width * height;
      if (bytes.Length - offset < pixelCount)
        throw new InvalidDataException($"'{path}' is truncated");
      var pixels = new byte[pixelCount];
      Array.Copy(bytes, offset, pixels, 0, pixelCount);
      return new GrayImage(width, height, width, pixels);
    }

    public static void Write(string path, GrayImage image)
    {
      using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
      var header = Encoding.ASCII.GetBytes($"P5\n{image.Width} {image.Height}\n255\n");
      stream.Write(header);
      for (int y = 0; y < image.Height; ++y)
        stream.Write(image.Row(y));
    }

    private static string ReadToken(byte[] bytes, ref int offset)
    {
      while (offset < bytes.Length)
      {
        if (bytes[offset] == (byte)'#')
        {
          while (offset < bytes.Length && bytes[offset] != (byte)'\n')
            ++offset;
        }
        else if (char.IsWhiteSpace((char)bytes[offset]))
          ++offset;
        else
          break;
      }
      int start = offset;
      while (offset < bytes.Length && !char.IsWhiteSpace((char)bytes[offset]))
        ++offset;
      if (start == offset)
        throw new InvalidDataException("Unexpected end of PGM header");
      return Encoding.ASCII.GetString(bytes, start, offset - start);
    }
  }
}
