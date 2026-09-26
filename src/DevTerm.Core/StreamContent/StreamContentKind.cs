namespace DevTerm.Core.StreamContent;

/// <summary>
/// One concrete kind of capturable content: its broad <see cref="Format"/>, a display name, the
/// file extension a capture of it is saved with (no leading dot), and its media type.
/// </summary>
public sealed record StreamContentKind(StreamContentFormat Format, string DisplayName, string Extension, string MediaType)
{
    public static readonly StreamContentKind Png = new(StreamContentFormat.Image, "PNG image", "png", "image/png");

    public static readonly StreamContentKind Jpeg = new(StreamContentFormat.Image, "JPEG image", "jpg", "image/jpeg");

    public static readonly StreamContentKind Gif = new(StreamContentFormat.Image, "GIF image", "gif", "image/gif");

    public static readonly StreamContentKind Bmp = new(StreamContentFormat.Image, "BMP image", "bmp", "image/bmp");

    public static readonly StreamContentKind Tiff = new(StreamContentFormat.Image, "TIFF image", "tif", "image/tiff");

    /// <summary>A declared <see cref="StreamContentFormat.Image"/> reply whose bytes matched no known image signature.</summary>
    public static readonly StreamContentKind UnknownImage = new(StreamContentFormat.Image, "image data (unrecognized format)", "bin", "application/octet-stream");

    public static readonly StreamContentKind Hpgl = new(StreamContentFormat.Hpgl, "HP-GL plot", "hpgl", "application/vnd.hp-hpgl");

    public static readonly StreamContentKind PostScript = new(StreamContentFormat.PostScript, "PostScript document", "ps", "application/postscript");

    public static readonly StreamContentKind Pcl = new(StreamContentFormat.Pcl, "PCL print job", "pcl", "application/vnd.hp-pcl");

    public static readonly StreamContentKind Binary = new(StreamContentFormat.Binary, "binary data", "bin", "application/octet-stream");

    /// <summary>
    /// Whether a front end can preview this kind with a platform's built-in raster decoders (WPF's
    /// <c>System.Windows.Media.Imaging</c> decodes all five) — HP-GL/PostScript/PCL need the
    /// not-yet-built rendering presenter (presenters.md §3) before they can be previewed.
    /// </summary>
    public bool IsNativeImage => Format == StreamContentFormat.Image && this != UnknownImage;

    /// <summary>The kind a declared <paramref name="format"/> hint falls back to when the reply's own bytes don't identify anything more specific.</summary>
    public static StreamContentKind ForDeclaredFormat(StreamContentFormat format) => format switch
    {
        StreamContentFormat.Hpgl => Hpgl,
        StreamContentFormat.PostScript => PostScript,
        StreamContentFormat.Pcl => Pcl,
        StreamContentFormat.Image => UnknownImage,
        _ => Binary,
    };
}
