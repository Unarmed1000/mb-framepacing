//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where the QR detector found a marker's finder and alignment pattern centres, in image pixels. Four points define the perspective transform
//* from module coordinates to the image, which is how a camera filming the screen is calibrated.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  /// <summary>
  /// Where the QR detector found a marker's finder and alignment pattern centres, in image pixels. Four points define the perspective
  /// transform from module coordinates (the symbol's top-left corner is (0, 0), one unit per module, quiet zone negative) to the image.
  /// </summary>
  public readonly record struct MarkerGeometry(ImagePoint TopLeft, ImagePoint TopRight, ImagePoint BottomLeft, ImagePoint Alignment)
  {
    /// <summary>The detector's four points, in the order <see cref="ModulePoints"/> uses.</summary>
    public ImagePoint[] ToArray() => new[] { TopLeft, TopRight, BottomLeft, Alignment };

    /// <summary>
    /// The four reference points in module coordinates: the finder pattern centres sit 3.5 modules inside the symbol corners, the bottom-right
    /// alignment pattern centre 6.5 modules in from the bottom-right corner (QR versions 2 and up).
    /// </summary>
    public static ImagePoint[] ModulePoints(int moduleCount)
    {
      double far = moduleCount - 3.5;
      double alignment = moduleCount - 6.5;
      return new[] { new ImagePoint(3.5, 3.5), new ImagePoint(far, 3.5), new ImagePoint(3.5, far), new ImagePoint(alignment, alignment) };
    }

    /// <summary>The transform from module coordinates to image pixels for a symbol with <paramref name="moduleCount"/> modules per side.</summary>
    public bool TryGetModuleToImage(int moduleCount, out Homography homography) =>
      Homography.TryFromPoints(ModulePoints(moduleCount), ToArray(), out homography);

    public MarkerGeometry Offset(double dx, double dy) =>
      new MarkerGeometry(TopLeft.Offset(dx, dy), TopRight.Offset(dx, dy), BottomLeft.Offset(dx, dy), Alignment.Offset(dx, dy));
  }
}
