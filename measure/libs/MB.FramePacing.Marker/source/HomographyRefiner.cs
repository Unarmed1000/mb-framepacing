//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Refines a marker's module to image transform by fitting the known module pattern to the image (Gauss-Newton). The QR detector's finder
//* and alignment centres are only good to about a pixel under perspective; the fit uses every module edge and gets well below that.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  /// <summary>
  /// Refines a marker's module to image transform by fitting the known module pattern to the image (Gauss-Newton). The QR detector's finder
  /// and alignment centres are only good to about a pixel under perspective; the fit uses every module edge and gets well below that.
  /// </summary>
  public static class HomographyRefiner
  {
    private const int SamplesPerModule = 4;
    private const int TemplatePxPerModule = 8;
    private const int QuietModules = 2;
    private const int ParameterCount = 10;
    private const double Step = 0.01;

    /// <summary>
    /// Fit the transform so the rendered <paramref name="modules"/> (dark = black, light and quiet zone = white, edges softened by
    /// <paramref name="edgeSoftnessModules"/>) best match <paramref name="image"/>. The parameters are the image positions of the four symbol
    /// corners, so all of them are in pixels and equally well conditioned.
    /// </summary>
    public static HomographyRefinement Refine(
      GrayImage image,
      Homography moduleToImage,
      ModuleMatrix modules,
      int maxIterations = 30,
      double tolerancePx = 0.002,
      double edgeSoftnessModules = 0.25
    )
    {
      ArgumentNullException.ThrowIfNull(image);
      ArgumentNullException.ThrowIfNull(modules);
      var template = BuildTemplate(modules, edgeSoftnessModules);
      int size = modules.Size;

      // Sample points in module space: the symbol plus a band of the quiet zone
      int span = (size + (2 * QuietModules)) * SamplesPerModule;
      int sampleCount = span * span;
      var sampleX = new double[sampleCount];
      var sampleY = new double[sampleCount];
      var model = new double[sampleCount];
      for (int j = 0; j < span; ++j)
      {
        for (int i = 0; i < span; ++i)
        {
          int k = (j * span) + i;
          sampleX[k] = -QuietModules + ((i + 0.5) / SamplesPerModule);
          sampleY[k] = -QuietModules + ((j + 0.5) / SamplesPerModule);
          model[k] = template.Sample(sampleX[k], sampleY[k]);
        }
      }

      var cornerModules = new ImagePoint[] { new(0, 0), new(size, 0), new(0, size), new(size, size) };
      var corners = new ImagePoint[4];
      for (int c = 0; c < 4; ++c)
        corners[c] = moduleToImage.Map(cornerModules[c]);

      var current = moduleToImage;
      double black = 0;
      double white = 255;
      bool photometricKnown = false;
      bool converged = false;
      double rms = double.PositiveInfinity;
      var perturbed = new Homography[8];
      var jacobianRow = new double[ParameterCount];
      var normal = new double[ParameterCount * (ParameterCount + 1)];
      var delta = new double[ParameterCount];

      for (int iteration = 0; iteration < maxIterations; ++iteration)
      {
        if (!Homography.TryFromPoints(cornerModules, corners, out current))
          break;
        for (int p = 0; p < 8; ++p)
        {
          var moved = (ImagePoint[])corners.Clone();
          moved[p / 2] = p % 2 == 0 ? moved[p / 2].Offset(Step, 0) : moved[p / 2].Offset(0, Step);
          if (!Homography.TryFromPoints(cornerModules, moved, out perturbed[p]))
            return new HomographyRefinement(moduleToImage, black, white, double.PositiveInfinity, false);
        }

        if (!photometricKnown)
        {
          (black, white) = FitLevels(image, current, sampleX, sampleY, model);
          photometricKnown = true;
        }

        Array.Clear(normal);
        double sumSquares = 0;
        int used = 0;
        for (int k = 0; k < sampleCount; ++k)
        {
          var q = new ImagePoint(sampleX[k], sampleY[k]);
          var x = current.Map(q);
          if (!Inside(image, x))
            continue;
          double observed = ImageWarp.SampleBilinear(image, x.X, x.Y);
          double gx = (ImageWarp.SampleBilinear(image, x.X + 0.5, x.Y) - ImageWarp.SampleBilinear(image, x.X - 0.5, x.Y));
          double gy = (ImageWarp.SampleBilinear(image, x.X, x.Y + 0.5) - ImageWarp.SampleBilinear(image, x.X, x.Y - 0.5));
          double predicted = black + ((white - black) * model[k]);
          double residual = observed - predicted;
          sumSquares += residual * residual;
          ++used;

          for (int p = 0; p < 8; ++p)
          {
            var moved = perturbed[p].Map(q);
            jacobianRow[p] = ((gx * (moved.X - x.X)) + (gy * (moved.Y - x.Y))) / Step;
          }
          // d(observed - predicted)/d(black) and /d(white)
          jacobianRow[8] = -(1 - model[k]);
          jacobianRow[9] = -model[k];
          for (int r = 0; r < ParameterCount; ++r)
          {
            for (int c = 0; c < ParameterCount; ++c)
              normal[(r * (ParameterCount + 1)) + c] += jacobianRow[r] * jacobianRow[c];
            normal[(r * (ParameterCount + 1)) + ParameterCount] -= jacobianRow[r] * residual;
          }
        }
        if (used < sampleCount / 2)
          break;
        rms = Math.Sqrt(sumSquares / used);

        // A little Levenberg damping keeps the first steps sane when the start is a pixel or two off
        for (int r = 0; r < ParameterCount; ++r)
          normal[(r * (ParameterCount + 1)) + r] *= 1.001;
        if (!LinearSolver.Solve(normal, ParameterCount, delta))
          break;

        double largest = 0;
        for (int c = 0; c < 4; ++c)
        {
          double dx = Math.Clamp(delta[2 * c], -2, 2);
          double dy = Math.Clamp(delta[(2 * c) + 1], -2, 2);
          corners[c] = corners[c].Offset(dx, dy);
          largest = Math.Max(largest, Math.Max(Math.Abs(dx), Math.Abs(dy)));
        }
        black += delta[8];
        white += delta[9];
        if (largest < tolerancePx)
        {
          converged = true;
          Homography.TryFromPoints(cornerModules, corners, out current);
          break;
        }
      }
      return new HomographyRefinement(current, black, white, rms, converged);
    }

    private static bool Inside(GrayImage image, ImagePoint point) =>
      point.X >= 1 && point.Y >= 1 && point.X < image.Width - 1 && point.Y < image.Height - 1;

    /// <summary>Least squares black and white levels for the current transform (observed = black + (white - black) * model).</summary>
    private static (double Black, double White) FitLevels(GrayImage image, Homography transform, double[] sampleX, double[] sampleY, double[] model)
    {
      double s00 = 0,
        s01 = 0,
        s11 = 0,
        b0 = 0,
        b1 = 0;
      for (int k = 0; k < model.Length; ++k)
      {
        var x = transform.Map(new ImagePoint(sampleX[k], sampleY[k]));
        if (!Inside(image, x))
          continue;
        double observed = ImageWarp.SampleBilinear(image, x.X, x.Y);
        double a = 1 - model[k];
        double b = model[k];
        s00 += a * a;
        s01 += a * b;
        s11 += b * b;
        b0 += a * observed;
        b1 += b * observed;
      }
      double determinant = (s00 * s11) - (s01 * s01);
      if (Math.Abs(determinant) < 1e-9)
        return (0, 255);
      return (((b0 * s11) - (b1 * s01)) / determinant, ((s00 * b1) - (s01 * b0)) / determinant);
    }

    private static Template BuildTemplate(ModuleMatrix modules, double softnessModules)
    {
      int size = modules.Size;
      int border = QuietModules + 1;
      int extent = (size + (2 * border)) * TemplatePxPerModule;
      var image = new GrayImage(extent, extent, 255);
      for (int y = 0; y < size; ++y)
      {
        for (int x = 0; x < size; ++x)
        {
          if (modules.IsDark(x, y))
          {
            image.FillRect(
              new PixelRect((x + border) * TemplatePxPerModule, (y + border) * TemplatePxPerModule, TemplatePxPerModule, TemplatePxPerModule),
              0
            );
          }
        }
      }
      return new Template(BoxBlur(image, (int)Math.Round(softnessModules * TemplatePxPerModule)), border);
    }

    /// <summary>Two box passes per axis: close enough to a Gaussian to soften the edges the fit follows.</summary>
    private static GrayImage BoxBlur(GrayImage image, int radius)
    {
      if (radius <= 0)
        return image;
      var result = image;
      for (int pass = 0; pass < 2; ++pass)
      {
        var horizontal = new GrayImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; ++y)
        {
          for (int x = 0; x < image.Width; ++x)
          {
            int sum = 0;
            for (int k = -radius; k <= radius; ++k)
              sum += result[Math.Clamp(x + k, 0, image.Width - 1), y];
            horizontal[x, y] = (byte)(sum / ((2 * radius) + 1));
          }
        }
        var vertical = new GrayImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; ++y)
        {
          for (int x = 0; x < image.Width; ++x)
          {
            int sum = 0;
            for (int k = -radius; k <= radius; ++k)
              sum += horizontal[x, Math.Clamp(y + k, 0, image.Height - 1)];
            vertical[x, y] = (byte)(sum / ((2 * radius) + 1));
          }
        }
        result = vertical;
      }
      return result;
    }

    /// <summary>The softened module pattern, sampled in module coordinates (0 = dark, 1 = light).</summary>
    private sealed class Template
    {
      private readonly GrayImage m_image;
      private readonly int m_border;

      public Template(GrayImage image, int border)
      {
        m_image = image;
        m_border = border;
      }

      public double Sample(double moduleX, double moduleY) =>
        ImageWarp.SampleBilinear(m_image, (moduleX + m_border) * TemplatePxPerModule, (moduleY + m_border) * TemplatePxPerModule, 255) / 255.0;
    }
  }
}
