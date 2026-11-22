namespace Normetry.Domain.Identity;

/// <summary>
/// The permission catalog, fixed in code as <c>area.action</c>. A data migration inserts the same
/// names into <c>core.permissions</c>, and a test checks that both agree. The Owner profile holds
/// every permission through <see cref="Profile.GrantsAll"/>, so a new one reaches it with no data
/// change; the tenant grants it to its other profiles.
/// </summary>
public static class Permissions
{
    public const int NameMaxLength = 100;

    private const char AreaSeparator = '.';
    private const string ReadAction = "read";

    public static class Establishments
    {
        public const string Read = "establishments.read";
        public const string Manage = "establishments.manage";
    }

    public static class Documents
    {
        public const string Read = "documents.read";

        /// <summary>Also covers uploading a new version.</summary>
        public const string Upload = "documents.upload";
        public const string Delete = "documents.delete";
    }

    public static class Chat
    {
        public const string Ask = "chat.ask";
    }

    public static class Inspections
    {
        /// <summary>Also covers watching a run's progress.</summary>
        public const string Read = "inspections.read";
        public const string Run = "inspections.run";

        /// <summary>Accepting, rejecting and editing findings, and marking actions done.</summary>
        public const string Review = "inspections.review";
    }

    public static class Profiles
    {
        public const string Read = "profiles.read";

        /// <summary>Also covers assigning profiles to users.</summary>
        public const string Manage = "profiles.manage";
    }

    public static readonly IReadOnlyList<string> All =
    [
        Establishments.Read,
        Establishments.Manage,
        Documents.Read,
        Documents.Upload,
        Documents.Delete,
        Chat.Ask,
        Inspections.Read,
        Inspections.Run,
        Inspections.Review,
        Profiles.Read,
        Profiles.Manage,
    ];

    /// <summary>
    /// The area of an <c>area.action</c> name: <c>documents</c> for <c>documents.upload</c>, empty
    /// for a name without one.
    /// </summary>
    public static string AreaOf(string name) => name[..Math.Max(name.IndexOf(AreaSeparator), 0)];

    /// <summary>
    /// The read permission of the name's area: <c>documents.read</c> for <c>documents.upload</c>.
    /// It may be outside the catalog.
    /// </summary>
    public static string ReadOf(string name) => $"{AreaOf(name)}{AreaSeparator}{ReadAction}";
}

/// <summary>A row of the catalog in <c>core.permissions</c>. The names are in <see cref="Permissions"/>.</summary>
public sealed class Permission
{
    private Permission(string name) => Name = name;

    public string Name { get; private set; }
}
