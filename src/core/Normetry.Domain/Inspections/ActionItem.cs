namespace Normetry.Domain.Inspections;

/// <summary>
/// The one action of a <see cref="Finding"/>: what to do and by when. It starts Open, from the
/// inspection or from a reviewer's edit, and a reviewer marks it done or reopens it.
/// </summary>
public sealed class ActionItem
{
    /// <summary>What the AI service may send; a reviewer's text is shorter.</summary>
    public const int TextMaxLength = 2000;
    public const int EditedTextMaxLength = 1000;

    private ActionItem(
        Guid id,
        Guid tenantId,
        Guid findingId,
        string text,
        DateOnly dueOn,
        Guid? updatedBy,
        DateTimeOffset? updatedAt
    )
    {
        Id = id;
        TenantId = tenantId;
        FindingId = findingId;
        Text = text;
        DueOn = dueOn;
        Status = ActionItemStatus.Open;
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid FindingId { get; private set; }

    /// <summary>English.</summary>
    public string Text { get; private set; }

    /// <summary>The last day to do it; a past day is allowed.</summary>
    public DateOnly DueOn { get; private set; }

    public ActionItemStatus Status { get; private set; }

    /// <summary>The <c>core.users</c> id of the last person who changed it; null when nobody has.</summary>
    public Guid? UpdatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    internal static ActionItem Create(
        Finding finding,
        string text,
        DateOnly dueOn,
        Guid? updatedBy,
        DateTimeOffset? updatedAt
    ) =>
        new(
            Guid.CreateVersion7(),
            finding.TenantId,
            finding.Id,
            Names.Required(text, TextMaxLength, nameof(text)),
            dueOn,
            updatedBy,
            updatedAt
        );

    public void MarkDone(Guid user, DateTimeOffset now) => Move(ActionItemStatus.Done, user, now);

    public void Reopen(Guid user, DateTimeOffset now) => Move(ActionItemStatus.Open, user, now);

    /// <summary>A reviewer's edit of the finding; the caller checked the text.</summary>
    internal void Change(string? text, DateOnly? dueOn, Guid user, DateTimeOffset now)
    {
        Text = text ?? Text;
        DueOn = dueOn ?? DueOn;
        UpdatedBy = user;
        UpdatedAt = now;
    }

    /// <summary>Marking it done twice changes nothing, not even who did it.</summary>
    private void Move(ActionItemStatus status, Guid user, DateTimeOffset now)
    {
        Names.RequiredId(user, nameof(user));
        if (Status != status)
        {
            Status = status;
            UpdatedBy = user;
            UpdatedAt = now;
        }
    }
}

public enum ActionItemStatus
{
    Open = 1,
    Done,
}
