//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A plane to plane perspective transform (3x3 matrix, H33 = 1). A camera filming a flat screen maps screen (or marker module) coordinates to
//* camera pixels through one of these.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  /// <summary>
  /// A plane to plane perspective transform (3x3 matrix, H33 = 1). A camera filming a flat screen maps screen (or marker module) coordinates
  /// to camera pixels through one of these.
  /// </summary>
  public readonly record struct Homography(double H11, double H12, double H13, double H21, double H22, double H23, double H31, double H32)
  {
    public static readonly Homography Identity = new Homography(1, 0, 0, 0, 1, 0, 0, 0);

    /// <summary>Map a point. Points on the horizon (w = 0) map to NaN.</summary>
    public ImagePoint Map(ImagePoint point)
    {
      double w = (H31 * point.X) + (H32 * point.Y) + 1;
      if (Math.Abs(w) < 1e-12)
        return new ImagePoint(double.NaN, double.NaN);
      return new ImagePoint(((H11 * point.X) + (H12 * point.Y) + H13) / w, ((H21 * point.X) + (H22 * point.Y) + H23) / w);
    }

    /// <summary>The inverse transform, or false when the matrix is singular.</summary>
    public bool TryInvert(out Homography inverse)
    {
      // Adjugate of [[a b c] [d e f] [g h 1]], then normalised so the last element is 1
      double a = H11,
        b = H12,
        c = H13,
        d = H21,
        e = H22,
        f = H23,
        g = H31,
        h = H32;
      double i11 = e - (f * h);
      double i12 = (c * h) - b;
      double i13 = (b * f) - (c * e);
      double i21 = (f * g) - d;
      double i22 = a - (c * g);
      double i23 = (c * d) - (a * f);
      double i31 = (d * h) - (e * g);
      double i32 = (b * g) - (a * h);
      double i33 = (a * e) - (b * d);
      double determinant = (a * i11) + (b * i21) + (c * i31);
      if (Math.Abs(determinant) < 1e-12 || Math.Abs(i33) < 1e-12)
      {
        inverse = default;
        return false;
      }
      inverse = new Homography(i11 / i33, i12 / i33, i13 / i33, i21 / i33, i22 / i33, i23 / i33, i31 / i33, i32 / i33);
      return true;
    }

    /// <summary>
    /// The transform that maps each of the four <paramref name="from"/> points to the matching <paramref name="to"/> point. Fails when three of
    /// the points are (nearly) collinear.
    /// </summary>
    public static bool TryFromPoints(ReadOnlySpan<ImagePoint> from, ReadOnlySpan<ImagePoint> to, out Homography homography)
    {
      if (from.Length != 4 || to.Length != 4)
        throw new ArgumentException("A homography needs exactly four point pairs");

      // Normalise both point sets (centre at the origin, mean distance sqrt 2) so the solve is well conditioned for camera sized coordinates
      var fromNorm = Normalization(from);
      var toNorm = Normalization(to);

      // Two rows per pair: u = (h11 x + h12 y + h13) / (h31 x + h32 y + 1), v likewise
      Span<double> matrix = stackalloc double[8 * 9];
      for (int i = 0; i < 4; ++i)
      {
        var p = fromNorm.Map(from[i]);
        var q = toNorm.Map(to[i]);
        var row = matrix.Slice(2 * i * 9, 9);
        row[0] = p.X;
        row[1] = p.Y;
        row[2] = 1;
        row[3] = 0;
        row[4] = 0;
        row[5] = 0;
        row[6] = -p.X * q.X;
        row[7] = -p.Y * q.X;
        row[8] = q.X;
        row = matrix.Slice(((2 * i) + 1) * 9, 9);
        row[0] = 0;
        row[1] = 0;
        row[2] = 0;
        row[3] = p.X;
        row[4] = p.Y;
        row[5] = 1;
        row[6] = -p.X * q.Y;
        row[7] = -p.Y * q.Y;
        row[8] = q.Y;
      }

      Span<double> h = stackalloc double[8];
      if (!LinearSolver.Solve(matrix, 8, h))
      {
        homography = default;
        return false;
      }
      var normalized = new Homography(h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7]);
      if (!toNorm.TryInvert(out var toDenorm))
      {
        homography = default;
        return false;
      }
      homography = Multiply(toDenorm, Multiply(normalized, fromNorm));
      return true;
    }

    /// <summary>The composition that applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
    public static Homography Multiply(Homography second, Homography first)
    {
      static double Get(in Homography m, int row, int column) =>
        ((row * 3) + column) switch
        {
          0 => m.H11,
          1 => m.H12,
          2 => m.H13,
          3 => m.H21,
          4 => m.H22,
          5 => m.H23,
          6 => m.H31,
          7 => m.H32,
          _ => 1,
        };

      Span<double> result = stackalloc double[9];
      for (int row = 0; row < 3; ++row)
      {
        for (int column = 0; column < 3; ++column)
        {
          double sum = 0;
          for (int k = 0; k < 3; ++k)
            sum += Get(second, row, k) * Get(first, k, column);
          result[(row * 3) + column] = sum;
        }
      }
      double scale = result[8];
      return new Homography(
        result[0] / scale,
        result[1] / scale,
        result[2] / scale,
        result[3] / scale,
        result[4] / scale,
        result[5] / scale,
        result[6] / scale,
        result[7] / scale
      );
    }

    private static Homography Normalization(ReadOnlySpan<ImagePoint> points)
    {
      double cx = 0;
      double cy = 0;
      foreach (var point in points)
      {
        cx += point.X;
        cy += point.Y;
      }
      cx /= points.Length;
      cy /= points.Length;
      double mean = 0;
      foreach (var point in points)
        mean += ImagePoint.Distance(point, new ImagePoint(cx, cy));
      mean /= points.Length;
      double scale = mean > 1e-12 ? Math.Sqrt(2) / mean : 1;
      return new Homography(scale, 0, -scale * cx, 0, scale, -scale * cy, 0, 0);
    }
  }
}
