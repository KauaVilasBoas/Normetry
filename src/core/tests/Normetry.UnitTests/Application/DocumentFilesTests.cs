using System.Text;
using Normetry.Application.Documents;
using Normetry.Domain.Documents;
using Xunit;

namespace Normetry.UnitTests.Application;

public sealed class DocumentFilesTests
{
    private static readonly byte[] Text = Encoding.UTF8.GetBytes("Cold storage log");

    [Fact]
    public void A_name_with_an_extension_longer_than_the_limit_is_an_unsupported_format()
    {
        var file = new UploadedFile("a." + new string('x', 300), "text/plain", Text);

        var result = DocumentFiles.Inspect(file);

        Assert.Equal(DocumentErrors.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void A_long_name_is_cut_to_the_limit_between_characters_and_keeps_its_extension()
    {
        // Each emoji is two chars, so the 251 chars left before ".txt" would end inside one.
        var file = new UploadedFile(
            string.Concat(Enumerable.Repeat("\U0001F600", 130)) + ".txt",
            "text/plain",
            Text
        );

        var name = DocumentFiles.Inspect(file).Value.FileName;

        Assert.True(name.Length <= DocumentVersion.FileNameMaxLength);
        Assert.EndsWith(".txt", name);
        Assert.False(char.IsHighSurrogate(name[^5]));
    }
}
