using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Tadmor.Printing;

/// <summary>
/// A minimal PDF writer for printable business documents, ported from
/// tadmor's internal/pdf. Pages use the standard-14 Helvetica fonts, which
/// every conforming reader provides, so no font program is embedded; only
/// their advance widths are needed, to measure text.
///
/// Coordinates are PDF-native: points, origin bottom-left, y upward. Text is
/// WinAnsi (CP1252); characters outside it render as '?'.
/// </summary>
internal sealed class Pdf
{
    public enum Font { Helvetica, HelveticaBold }

    private readonly List<Page> pages = [];

    public IReadOnlyList<Page> Pages => pages;

    /// <summary>Appends a page of the given size in points (A4 is 595.28 × 841.89).</summary>
    public Page AddPage(double width, double height)
    {
        var p = new Page(width, height);
        pages.Add(p);
        return p;
    }

    /// <summary>The rendered width of s in points.</summary>
    public static double Width(Font f, double size, string s)
    {
        var widths = f == Font.HelveticaBold ? HelveticaMetrics.Bold : HelveticaMetrics.Regular;
        var units = 0;
        foreach (var b in WinAnsi(s))
        {
            units += widths[b];
        }
        return units * size / 1000;
    }

    public sealed class Page(double width, double height)
    {
        public double Width { get; } = width;
        public double Height { get; } = height;
        internal readonly MemoryStream Content = new();

        public void Text(Font f, double size, double x, double y, string s) => TextGray(f, size, x, y, 0, s);

        /// <summary>Draws s with its baseline at (x, y), in a gray level (0 black, 1 white).</summary>
        public void TextGray(Font f, double size, double x, double y, double gray, string s)
        {
            Write($"BT {Res(f)} {Num(size)} Tf {Num(gray)} g {Num(x)} {Num(y)} Td ");
            var escaped = Escape(WinAnsi(s));
            Content.Write(escaped);
            Write(" Tj ET\n");
        }

        public void Line(double x1, double y1, double x2, double y2, double width, double gray) =>
            Write($"{Num(width)} w {Num(gray)} G {Num(x1)} {Num(y1)} m {Num(x2)} {Num(y2)} l S\n");

        private void Write(string s) => Content.Write(Encoding.ASCII.GetBytes(s));
    }

    private static string Res(Font f) => f == Font.HelveticaBold ? "/F2" : "/F1";

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // The runes WinAnsi places in 0x80–0x9F, where Unicode and CP1252 diverge.
    private static readonly Dictionary<char, byte> Specials = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88,
        ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93,
        ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
        ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    private static byte[] WinAnsi(string s)
    {
        var bytes = new List<byte>(s.Length);
        foreach (var c in s)
        {
            if (c < 0x80 || (c >= 0xA0 && c <= 0xFF))
            {
                bytes.Add((byte)c);
            }
            else
            {
                bytes.Add(Specials.TryGetValue(c, out var b) ? b : (byte)'?');
            }
        }
        return bytes.ToArray();
    }

    private static byte[] Escape(byte[] text)
    {
        var o = new List<byte>(text.Length + 2) { (byte)'(' };
        foreach (var c in text)
        {
            switch (c)
            {
                case (byte)'(' or (byte)')' or (byte)'\\':
                    o.Add((byte)'\\');
                    o.Add(c);
                    break;
                case (byte)'\n':
                    o.AddRange("\\n"u8.ToArray());
                    break;
                case (byte)'\r':
                    o.AddRange("\\r"u8.ToArray());
                    break;
                default:
                    o.Add(c);
                    break;
            }
        }
        o.Add((byte)')');
        return o.ToArray();
    }

    /// <summary>
    /// Serializes the document: 1 catalog, 2 page tree, 3 and 4 the fonts,
    /// then a page object and a flate-compressed content stream per page.
    /// </summary>
    public byte[] Bytes()
    {
        var buf = new MemoryStream();
        void W(string s) => buf.Write(Encoding.ASCII.GetBytes(s));
        W("%PDF-1.4\n%");
        buf.Write([0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        var offsets = new List<long>();
        void Obj(string body)
        {
            offsets.Add(buf.Position);
            W($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        var kids = string.Concat(pages.Select((_, i) => $"{5 + 2 * i} 0 R "));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (var i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(p.Width)} {Num(p.Height)}] "
                + $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + 2 * i} 0 R >>");
            var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(p.Content.ToArray());
            }
            offsets.Add(buf.Position);
            W($"{offsets.Count} 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
            buf.Write(compressed.ToArray());
            W("\nendstream\nendobj\n");
        }

        var xref = buf.Position;
        W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets)
        {
            W($"{off:D10} 00000 n \n");
        }
        W($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return buf.ToArray();
    }
}
