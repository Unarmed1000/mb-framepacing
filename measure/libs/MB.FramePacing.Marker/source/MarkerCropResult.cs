//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The region of the source a fast capture stores (see MarkerCrop) and the integer area downscale applied to it.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  /// <summary>The region of the source a fast capture stores and the integer area downscale applied to it.</summary>
  /// <param name="Roi">Crop in source pixels; its size and its distance to the marker origin are multiples of <paramref name="Factor"/>.</param>
  /// <param name="Factor">Integer downscale of the crop (1 = stored at source resolution).</param>
  /// <param name="StoredModulePx">Marker module size in stored pixels.</param>
  public readonly record struct MarkerCropResult(PixelRect Roi, int Factor, float StoredModulePx)
  {
    public int StoredWidth => Roi.Width / Factor;
    public int StoredHeight => Roi.Height / Factor;
  }
}
