//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Rectifies a camera frame into the stored camera capture layout in C# (EXPERIMENTAL camera support): each rig zone's square is resampled to
//* CameraZone.StoredSizePx and the zones are stacked in scanout order, the same layout the ffmpeg camera filter produces. Used for sources
//* that do not go through ffmpeg (the synthetic camera, future camera SDKs). The rig never moves, so where every stored pixel samples the
//* camera frame is computed once; each frame is then a gather without allocations.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Camera
{
  public sealed class CameraRectifier
  {
    private const int WeightBits = 8;
    private const int WeightOne = 1 << WeightBits;

    private readonly CameraRig m_rig;

    // Per stored pixel: SamplesPerPixel taps of (camera offset of the top-left of a 2x2 block, its four bilinear weights)
    private readonly int m_samplesPerPixel;
    private readonly int[] m_offsets;
    private readonly ushort[] m_weights;

    public CameraRectifier(CameraRig rig)
    {
      m_rig = rig ?? throw new ArgumentNullException(nameof(rig));
      if (rig.CameraWidth < 2 || rig.CameraHeight < 2)
        throw new ArgumentException("The rig has no camera size", nameof(rig));

      // Average enough samples per stored pixel when the camera sees more pixels per module than are stored
      int grid = 1;
      foreach (var zone in rig.Zones)
        grid = Math.Max(grid, Math.Clamp((int)Math.Ceiling(zone.ModuleSizePx / CameraZone.StoredPxPerModule), 1, 4));
      m_samplesPerPixel = grid * grid;
      int pixels = Width * Height;
      m_offsets = new int[pixels * m_samplesPerPixel];
      m_weights = new ushort[pixels * m_samplesPerPixel * 4];

      double near = -CameraZone.MarginModules;
      double scale = 1.0 / CameraZone.StoredPxPerModule;
      var storedToModule = new Homography(scale, 0, near, 0, scale, near, 0, 0);
      for (int z = 0; z < rig.Zones.Count; ++z)
      {
        var storedToCamera = Homography.Multiply(rig.Zones[z].ModuleToCamera, storedToModule);
        for (int y = 0; y < CameraZone.StoredSizePx; ++y)
        {
          for (int x = 0; x < CameraZone.StoredSizePx; ++x)
          {
            int pixel = (((z * CameraZone.StoredSizePx) + y) * Width) + x;
            for (int sy = 0; sy < grid; ++sy)
            {
              for (int sx = 0; sx < grid; ++sx)
              {
                var point = storedToCamera.Map(new ImagePoint(x + ((sx + 0.5) / grid), y + ((sy + 0.5) / grid)));
                SetTap((pixel * m_samplesPerPixel) + (sy * grid) + sx, point);
              }
            }
          }
        }
      }
    }

    public int Width => CameraZone.StoredSizePx;

    public int Height => CameraZone.StoredSizePx * m_rig.Zones.Count;

    public void Rectify(GrayImage camera, GrayImage stored)
    {
      if (camera.Width != m_rig.CameraWidth || camera.Height != m_rig.CameraHeight)
        throw new ArgumentException($"The camera frame is {camera.Width}x{camera.Height}, the rig expects {m_rig.CameraWidth}x{m_rig.CameraHeight}");
      if (stored.Width != Width || stored.Height != Height)
        throw new ArgumentException($"The stored frame must be {Width}x{Height}");
      if (camera.Stride != camera.Width)
        throw new ArgumentException("The camera frame must be tightly packed");

      var source = camera.Pixels;
      int stride = camera.Stride;
      int taps = m_samplesPerPixel;
      int round = (taps * WeightOne) / 2;
      for (int y = 0; y < Height; ++y)
      {
        var row = stored.Row(y);
        int pixel = y * Width;
        for (int x = 0; x < row.Length; ++x, ++pixel)
        {
          int sum = 0;
          for (int t = 0; t < taps; ++t)
          {
            int tap = (pixel * taps) + t;
            int offset = m_offsets[tap];
            int w = tap * 4;
            sum +=
              (source[offset] * m_weights[w])
              + (source[offset + 1] * m_weights[w + 1])
              + (source[offset + stride] * m_weights[w + 2])
              + (source[offset + stride + 1] * m_weights[w + 3]);
          }
          row[x] = (byte)((sum + round) / (taps * WeightOne));
        }
      }
    }

    /// <summary>Bilinear weights around a camera position (pixel centres at +0.5), clamped to the frame.</summary>
    private void SetTap(int tap, ImagePoint point)
    {
      double fx = Math.Clamp(point.X - 0.5, 0, m_rig.CameraWidth - 1.001);
      double fy = Math.Clamp(point.Y - 0.5, 0, m_rig.CameraHeight - 1.001);
      if (double.IsNaN(fx) || double.IsNaN(fy))
        fx = fy = 0;
      int x0 = (int)fx;
      int y0 = (int)fy;
      double ax = fx - x0;
      double ay = fy - y0;
      m_offsets[tap] = (y0 * m_rig.CameraWidth) + x0;
      // Integer weights that always sum to WeightOne
      int w00 = (int)Math.Round((1 - ax) * (1 - ay) * WeightOne);
      int w10 = (int)Math.Round(ax * (1 - ay) * WeightOne);
      int w01 = (int)Math.Round((1 - ax) * ay * WeightOne);
      int w11 = WeightOne - w00 - w10 - w01;
      m_weights[tap * 4] = (ushort)w00;
      m_weights[(tap * 4) + 1] = (ushort)w10;
      m_weights[(tap * 4) + 2] = (ushort)w01;
      m_weights[(tap * 4) + 3] = (ushort)Math.Max(0, w11);
    }
  }
}
