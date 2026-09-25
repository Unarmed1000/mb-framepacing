//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of HomographyRefiner: the fitted module to image transform and how well the marker model explains the image.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  /// <summary>The outcome of <see cref="HomographyRefiner"/>: the fitted module to image transform and how well the marker model fits.</summary>
  /// <param name="ModuleToImage">The refined transform from module coordinates to image pixels.</param>
  /// <param name="Black">Fitted image luma of a dark module.</param>
  /// <param name="White">Fitted image luma of a light module (and the quiet zone).</param>
  /// <param name="RmsResidual">Root mean square difference between the image and the fitted model, in luma steps.</param>
  /// <param name="Converged">The fit settled (the last step moved every corner less than the tolerance).</param>
  public readonly record struct HomographyRefinement(Homography ModuleToImage, double Black, double White, double RmsResidual, bool Converged)
  {
    public double Contrast => White - Black;

    /// <summary>Residual relative to the contrast: about 0.1 for a sharp, settled marker; high for blur, a mixed frame or a wrong fit.</summary>
    public double RelativeResidual => Contrast > 1 ? RmsResidual / Contrast : double.PositiveInfinity;
  }
}
