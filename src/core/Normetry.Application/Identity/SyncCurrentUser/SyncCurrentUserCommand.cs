using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.SyncCurrentUser;

/// <summary>
/// Creates the caller's user row on their first sign-in, with no profiles, and keeps its display
/// name and email in step with the token. Idempotent.
/// </summary>
public sealed record SyncCurrentUserCommand : ICommand;
