//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The four point perspective solve, mapping and inversion.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class HomographyTests
  {
    private static readonly ImagePoint[] g_square = { new(0, 0), new(1000, 0), new(0, 1000), new(1000, 1000) };
    private static readonly ImagePoint[] g_camera = { new(130, 42.5), new(1710.25, 95), new(160, 1020), new(1650, 990.75) };

    [Test]
    public void FromPoints_MapsEachPointExactly()
    {
      Assert.That(Homography.TryFromPoints(g_square, g_camera, out var homography), Is.True);
      for (int i = 0; i < 4; ++i)
      {
        var mapped = homography.Map(g_square[i]);
        Assert.That(mapped.X, Is.EqualTo(g_camera[i].X).Within(1e-6));
        Assert.That(mapped.Y, Is.EqualTo(g_camera[i].Y).Within(1e-6));
      }
    }

    [Test]
    public void Inverse_RoundTrips()
    {
      Assert.That(Homography.TryFromPoints(g_square, g_camera, out var homography), Is.True);
      Assert.That(homography.TryInvert(out var inverse), Is.True);
      foreach (var point in new ImagePoint[] { new(0, 0), new(250, 750), new(999, 1), new(500, 500) })
      {
        var back = inverse.Map(homography.Map(point));
        Assert.That(back.X, Is.EqualTo(point.X).Within(1e-6));
        Assert.That(back.Y, Is.EqualTo(point.Y).Within(1e-6));
      }
    }

    [Test]
    public void Multiply_AppliesFirstThenSecond()
    {
      var scale = new Homography(2, 0, 0, 0, 3, 0, 0, 0);
      var shift = new Homography(1, 0, 10, 0, 1, 20, 0, 0);
      var point = Homography.Multiply(shift, scale).Map(new ImagePoint(1, 1));
      Assert.That(point, Is.EqualTo(new ImagePoint(12, 23)));
    }

    [Test]
    public void FromPoints_RejectsCollinearPoints()
    {
      var line = new ImagePoint[] { new(0, 0), new(1, 1), new(2, 2), new(3, 3) };
      Assert.That(Homography.TryFromPoints(line, g_camera, out _), Is.False);
    }
  }
}
