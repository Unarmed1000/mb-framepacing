//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The ffmpeg input device family.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <summary>The ffmpeg input device family.</summary>
  public enum FfmpegInputKind
  {
    /// <summary>Windows DirectShow ('-f dshow').</summary>
    DirectShow,

    /// <summary>Linux Video4Linux2 ('-f v4l2').</summary>
    Video4Linux2,

    /// <summary>macOS AVFoundation ('-f avfoundation').</summary>
    AVFoundation,

    /// <summary>ffmpeg's built in test sources ('-f lavfi'), for trying the pipeline without hardware.</summary>
    Lavfi,

    /// <summary>Anything ffmpeg opens with a plain '-i': a video file or a stream URL (rtsp://, srt://, udp://, http://...).</summary>
    Media,

    /// <summary>A folder of images, played through an ffconcat list with per-frame durations (see <see cref="ImageSequence"/>).</summary>
    ImageSequence,
  }
}
