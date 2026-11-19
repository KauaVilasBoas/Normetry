using Normetry.Domain.Documents;
using Xunit;

namespace Normetry.UnitTests.Domain;

public sealed class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Establishment = Guid.NewGuid();
    private static readonly Guid Uploader = Guid.NewGuid();
    private static readonly DocumentFile Pdf = new(
        "SOP.pdf",
        ".pdf",
        "application/pdf",
        10,
        new string('a', 64)
    );

    private static Document NewDocument() =>
        Document.Create(
            Tenant,
            Establishment,
            "Cold storage SOP",
            DocumentType.Sop,
            Pdf,
            Uploader,
            Now
        );

    [Fact]
    public void A_new_document_has_one_uploaded_version_under_its_tenant_path()
    {
        var document = NewDocument();

        var version = Assert.Single(document.Versions);
        Assert.Equal((1, DocumentVersionStatus.Uploaded), (version.Number, version.Status));
        Assert.Equal(
            $"tenants/{Tenant}/establishments/{Establishment}/documents/{document.Id}/versions/{version.Id}/file.pdf",
            version.BlobPath
        );
        Assert.Null(document.Current);
        Assert.True(document.HasVersionInFlight);
    }

    [Fact]
    public void Status_runs_uploaded_processing_indexed_and_ignores_late_or_repeated_events()
    {
        var version = NewDocument().Newest;

        Assert.True(version.StartProcessing(Now));
        Assert.False(version.StartProcessing(Now));
        Assert.True(version.MarkIndexed(12, 3, "en", Now.AddMinutes(1)));
        Assert.False(version.MarkIndexed(12, 3, "en", Now.AddMinutes(2)));
        // Neither a late start nor a late failure undoes an indexed version.
        Assert.False(version.StartProcessing(Now));
        Assert.False(version.MarkFailed("internal", "Boom.", Now));
        Assert.Equal(
            (DocumentVersionStatus.Indexed, 12, 3, "en", Now.AddMinutes(1)),
            (
                version.Status,
                version.ChunkCount,
                version.PageCount,
                version.Language,
                version.StatusChangedAt
            )
        );
    }

    [Fact]
    public void Only_a_processing_version_times_out_and_a_late_index_still_wins()
    {
        var timeout = TimeSpan.FromMinutes(15);
        var version = NewDocument().Newest;

        Assert.False(version.TimeOut(timeout, Now.AddHours(1))); // Uploaded waits for the AI service
        version.StartProcessing(Now);
        Assert.False(version.TimeOut(timeout, Now.AddMinutes(14)));
        Assert.True(version.TimeOut(timeout, Now.AddMinutes(15)));
        Assert.Equal(
            (DocumentVersionStatus.Failed, DocumentIngestionFailureReasons.TimedOut),
            (version.Status, version.FailureCode)
        );

        Assert.True(version.MarkIndexed(4, null, "pt", Now.AddMinutes(20)));
        Assert.Equal((DocumentVersionStatus.Indexed, null), (version.Status, version.FailureCode));
    }

    [Fact]
    public void A_new_version_waits_for_the_newest_and_a_failed_one_keeps_the_previous_current()
    {
        var document = NewDocument();
        Assert.Throws<InvalidOperationException>(() => document.AddVersion(Pdf, Uploader, Now));

        document.Newest.MarkIndexed(1, 1, "en", Now);
        var second = document.AddVersion(Pdf, Uploader, Now);
        Assert.Equal((2, 1), (second.Number, document.Current!.Number));

        second.MarkFailed("no_text_layer", "No text.", Now);
        Assert.False(document.HasVersionInFlight);
        Assert.Equal(1, document.Current!.Number);
        Assert.Equal(3, document.AddVersion(Pdf, Uploader, Now).Number);
    }

    [Fact]
    public void A_deleted_document_gets_no_new_version()
    {
        var document = NewDocument();
        document.Newest.MarkFailed("parse_error", "Unreadable.", Now);
        document.Delete(Now);

        Assert.True(document.IsDeleted);
        Assert.Throws<InvalidOperationException>(() => document.AddVersion(Pdf, Uploader, Now));
    }

    [Fact]
    public void The_expiry_date_is_optional_any_date_and_can_be_cleared_until_the_document_is_deleted()
    {
        var document = NewDocument();
        Assert.Null(document.ExpiresOn);

        document.SetExpiry(new DateOnly(2020, 1, 31)); // in the past: allowed, the document is expired
        Assert.Equal(new DateOnly(2020, 1, 31), document.ExpiresOn);
        document.SetExpiry(null);
        Assert.Null(document.ExpiresOn);

        document.SetExpiry(new DateOnly(2027, 6, 30));
        document.Delete(Now);
        Assert.Throws<InvalidOperationException>(() => document.SetExpiry(null));
        Assert.Equal(new DateOnly(2027, 6, 30), document.ExpiresOn);
    }
}
