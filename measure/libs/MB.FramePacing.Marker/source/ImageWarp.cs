//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Perspective resampling of grayscale images: how a camera sees a flat screen. Used by the synthetic camera and the tests.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  /// <summary>Perspective resampling of grayscale images: how a camera sees a flat screen. Used by the synthetic camera and the tests.</summary>
  public static class ImageWarp
  {
    /// <summary>
    /// Fill <paramref name="destination"/> by sampling <paramref name="source"/> through <paramref name="destinationToSource"/>. Each destination
    /// pixel averages <paramref name="supersample"/>² bilinear samples, which models a sensor pixel integrating the light that falls on it.
    /// Samples outside the source read <paramref name="background"/>.
    /// </summary>
    public static void Warp(GrayImage source, Homography destinationToSource, GrayImage destination, int supersample = 3, byte background = 0)
    {
      if (supersample < 1)
        throw new ArgumentOutOfRangeException(nameof(supersample));
      double step = 1.0 / supersample;
      int sampleCount = supersample * supersample;
      for (int y = 0; y < destination.Height; ++y)
      {
        for (int x = 0; x < destination.Width; ++x)
        {
          double sum = 0;
          for (int sy = 0; sy < supersample; ++sy)
          {
            for (int sx = 0; sx < supersample; ++sx)
            {
              var point = destinationToSource.Map(new ImagePoint(x + ((sx + 0.5) * step), y + ((sy + 0.5) * step)));
              sum += SampleBilinear(source, point.X, point.Y, background);
            }
          }
          destination[x, y] = (byte)Math.Clamp(Math.Round(sum / sampleCount), 0, 255);
        }
      }
    }

    /// <summary>Bilinear sample at a subpixel position (pixel centres at +0.5). Outside the image reads <paramref name="background"/>.</summary>
    public static double SampleBilinear(GrayImage image, double x, double y, byte background = 0)
    {
      if (double.IsNaN(x) || double.IsNaN(y))
        return background;
      double fx = x - 0.5;
      double fy = y - 0.5;
      int x0 = (int)Math.Floor(fx);
      int y0 = (int)Math.Floor(fy);
      double ax = fx - x0;
      double ay = fy - y0;
      double top = (Read(image, x0, y0, background) * (1 - ax)) + (Read(image, x0 + 1, y0, background) * ax);
      double bottom = (Read(image, x0, y0 + 1, background) * (1 - ax)) + (Read(image, x0 + 1, y0 + 1, background) * ax);
      return (top * (1 - ay)) + (bottom * ay);
    }

    private static byte Read(GrayImage image, int x, int y, byte background) =>
      (uint)x < (uint)image.Width && (uint)y < (uint)image.Height ? image.Pixels[(y * image.Stride) + x] : background;
  }
}
