namespace Normetry.Application.Abstractions;

/// <summary>
/// Marks a request whose handler never touches the database, so no transaction is opened for it.
/// Every other request runs in a tenant-scoped transaction. A handler that queries the database
/// anyway fails: a command outside a transaction is rejected.
/// </summary>
public interface INoTransaction;
