using System;

namespace ImageConverterPoc
{
    public enum ImageFormatKind
    {
        Unknown = 0,
        TiffGroup4,
        TiffLzw,
        PdfMinimal
    }

    public static class ImageFormatKindParser
    {
        public static ImageFormatKind Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ImageFormatKind.Unknown;

            switch (value.Trim().ToUpperInvariant())
            {
                case "FF_TIFG4":
                    return ImageFormatKind.TiffGroup4;
                case "FF_TIFLZW":
                    return ImageFormatKind.TiffLzw;
                case "FF_PDF_MIN":
                    return ImageFormatKind.PdfMinimal;
                default:
                    return ImageFormatKind.Unknown;
            }
        }
    }
}
