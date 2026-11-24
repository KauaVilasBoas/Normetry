using System.Text;
using Normetry.Application.Documents;
using Normetry.Domain.Documents;
using Xunit;

namespace Normetry.UnitTests.Application;

public sealed class DocumentFilesTests
{
    private const string Emoji = "\U0001F600";

    /// <summary>The characters left before a ".txt" extension.</summary>
    private const int StemMaxLength = DocumentVersion.FileNameMaxLength - 4;

    private static readonly byte[] Text = Encoding.UTF8.GetBytes("Cold storage log");

    [Fact]
    public void A_name_with_an_extension_longer_than_the_limit_is_an_unsupported_format()
    {
        var file = new UploadedFile("a." + new string('x', 300), "text/plain", Text);

        var result = DocumentFiles.Inspect(file);

        Assert.Equal(DocumentErrors.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void A_name_at_the_limit_in_astral_characters_is_kept_whole()
    {
        // 255 characters, but 506 UTF-16 units: each emoji is a surrogate pair.
        var fileName = string.Concat(Enumerable.Repeat(Emoji, StemMaxLength)) + ".txt";

        var name = DocumentFiles
            .Inspect(new UploadedFile(fileName, "text/plain", Text))
            .Value.FileName;

        Assert.Equal(fileName, name);
    }

    [Fact]
    public void A_long_name_is_cut_to_the_limit_between_characters_and_keeps_its_extension()
    {
        var file = new UploadedFile(
            string.Concat(Enumerable.Repeat(Emoji, 300)) + ".txt",
            "text/plain",
            Text
        );

        var name = DocumentFiles.Inspect(file).Value.FileName;

        // Whole emoji only: a cut inside a surrogate pair would not equal this.
        Assert.Equal(string.Concat(Enumerable.Repeat(Emoji, StemMaxLength)) + ".txt", name);
    }
}
