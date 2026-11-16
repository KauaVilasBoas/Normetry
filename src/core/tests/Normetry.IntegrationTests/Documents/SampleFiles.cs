using System.IO.Compression;
using System.Text;

namespace Normetry.IntegrationTests.Documents;

/// <summary>Small files of each accepted format, built in code.</summary>
public static class SampleFiles
{
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"
    );

    public static readonly byte[] Docx = Zip("word/document.xml", "[Content_Types].xml");

    /// <summary>A zip, but not a Word document.</summary>
    public static readonly byte[] PlainZip = Zip("notes.txt");

    public static readonly byte[] Txt = Encoding.UTF8.GetBytes(
        "Keep cold food at 5 °C or below.\n"
    );

    public static readonly byte[] Csv = Encoding.UTF8.GetBytes(
        "date,unit,temperature\n2026-11-14,walk-in,3.5\n"
    );

    public const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static byte[] Zip(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write("<xml/>");
            }
        }
        return buffer.ToArray();
    }
}
