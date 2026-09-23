//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How large the marker is drawn: the module size in source pixels and the quiet zone around the symbol.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public readonly struct Options
  {
    public Options(int moduleSizePx, int quietZoneModules = Marker.RecommendedQuietZoneModules)
    {
      ModuleSizePx = moduleSizePx;
      QuietZoneModules = quietZoneModules;
    }

    /// <summary>6 pixel modules and the recommended quiet zone, the same defaults as the C++ library.</summary>
    public static Options Default => new Options(6, Marker.RecommendedQuietZoneModules);

    /// <summary>Size of one QR module in source pixels. See doc/marker-format.md "Sizing" or <see cref="Marker.RecommendModuleSizePx"/>.</summary>
    public int ModuleSizePx { get; }

    /// <summary>White border around the symbol in modules. The QR specification asks for 4.</summary>
    public int QuietZoneModules { get; }
  }
}
