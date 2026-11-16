namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Work a scope's command runs only once its transaction has committed (<c>TransactionBehavior</c>
/// runs it). Nothing runs after a rollback.
/// </summary>
internal sealed class AfterCommit
{
    private readonly List<Action> actions = [];

    public void Add(Action action) => actions.Add(action);

    public void Run()
    {
        foreach (var action in actions)
        {
            action();
        }
        actions.Clear();
    }
}
