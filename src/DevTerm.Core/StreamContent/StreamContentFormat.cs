namespace DevTerm.Core.StreamContent;

/// <summary>
/// What a device reply is expected (or detected) to contain, as far as the Stream Monitor cares —
/// see docs/design/proposals/stream-content-detection.md. <see cref="Text"/> (the default) means
/// "an ordinary reply, nothing to capture"; every other value is something worth capturing and
/// saving as a file rather than only showing as text/hex.
/// </summary>
public enum StreamContentFormat
{
    /// <summary>An ordinary text reply — not captured.</summary>
    Text,

    /// <summary>An HP-GL / HP-GL/2 plotter command stream.</summary>
    Hpgl,

    /// <summary>A PostScript (or Encapsulated PostScript) document.</summary>
    PostScript,

    /// <summary>A PCL printer job.</summary>
    Pcl,

    /// <summary>A raster image (BMP, PNG, JPEG, GIF, TIFF).</summary>
    Image,

    /// <summary>Opaque binary data with no recognizable signature.</summary>
    Binary,
}
