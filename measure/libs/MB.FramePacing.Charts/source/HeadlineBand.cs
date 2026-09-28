//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The headline band on top of the report's Timeline image: the run's title, its headline tiles (RunHeadline, the same the GUI shows) and
//* the cause of its animation error, so the image says what it shows without the GUI next to it.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using ScottPlot;
using SkiaSharp;

namespace MB.FramePacing.Charts
{
  internal static class HeadlineBand
  {
    private const float Padding = 20;
    private const float TitleSize = 18;
    private const float TileHeight = 64;
    private const float TileGap = 12;
    private const float MinTileWidth = 170;
    private const float CaptionSize = 12;
    private const float ValueSize = 22;
    private const float CauseSize = 13;

    /// <summary>Draws the band of <paramref name="run"/> at the given width; its height follows from how many rows of tiles fit.</summary>
    public static SKImage Render(ChartRun run, ChartTheme theme, int width)
    {
      var tiles = RunHeadline.Tiles(run);
      string title = RunHeadline.Title(run.Run) + (run.Run.StartTimeUtc is { } start ? $", started {start:yyyy-MM-dd HH:mm:ss} UTC" : string.Empty);
      string? cause = run.Run.Pacing is { } pacing ? "Cause: " + RunHeadline.Cause(pacing) : null;

      int columns = Columns(tiles.Count, width);
      int rows = (tiles.Count + columns - 1) / columns;
      float tileWidth = (width - (2 * Padding) - ((columns - 1) * TileGap)) / columns;
      float tilesTop = Padding + TitleSize + 14;
      float tilesBottom = tilesTop + (rows * TileHeight) + ((rows - 1) * TileGap);
      int height = (int)Math.Ceiling(tilesBottom + (cause != null ? 14 + CauseSize : 0) + Padding);

      using var surface = SKSurface.Create(new SKImageInfo(width, height));
      var canvas = surface.Canvas;
      canvas.Clear(theme.Background.ToSKColor());
      using var typeface = SKTypeface.FromFamilyName(Fonts.Default) ?? SKTypeface.Default;
      using var bold = SKTypeface.FromFamilyName(Fonts.Default, SKFontStyle.Bold) ?? SKTypeface.Default;
      using var paint = new SKPaint { IsAntialias = true };
      var subtle = theme.Foreground.WithAlpha(0.7).ToSKColor();

      void Text(string text, float x, float y, SKTypeface face, float size, SKColor color)
      {
        using var font = new SKFont(face, size);
        paint.Color = color;
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
      }

      float Width(string text, SKTypeface face, float size)
      {
        using var font = new SKFont(face, size);
        return font.MeasureText(text);
      }

      Text(title, Padding, Padding + TitleSize, bold, TitleSize, theme.Foreground.ToSKColor());
      for (int i = 0; i < tiles.Count; ++i)
      {
        var tile = tiles[i];
        float x = Padding + ((i % columns) * (tileWidth + TileGap));
        float y = tilesTop + ((i / columns) * (TileHeight + TileGap));
        var box = new SKRoundRect(new SKRect(x, y, x + tileWidth, y + TileHeight), 10);
        paint.Style = SKPaintStyle.Fill;
        paint.Color = theme.Foreground.WithAlpha(0.04).ToSKColor();
        canvas.DrawRoundRect(box, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = theme.Grid.ToSKColor();
        canvas.DrawRoundRect(box, paint);

        Text(tile.Caption, x + 14, y + 22, typeface, CaptionSize, subtle);
        var valueColor = (tile.Warning ? theme.Warning : theme.Foreground).ToSKColor();
        Text(tile.Value, x + 14, y + 50, bold, ValueSize, valueColor);
        if (tile.Detail.Length > 0)
          Text(tile.Detail, x + 14 + Width(tile.Value, bold, ValueSize) + 6, y + 50, typeface, CaptionSize, subtle);
      }
      if (cause != null)
        Text(cause, Padding, tilesBottom + 14 + CauseSize, typeface, CauseSize, theme.Foreground.ToSKColor());
      return surface.Snapshot();
    }

    /// <summary>As many tiles side by side as fit at <see cref="MinTileWidth"/>; more wrap onto further rows.</summary>
    internal static int Columns(int tiles, int width) =>
      Math.Max(1, Math.Min(tiles, (int)((width - (2 * Padding) + TileGap) / (MinTileWidth + TileGap))));
  }
}
