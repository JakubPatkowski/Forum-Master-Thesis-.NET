namespace Forum.Common.Security;

/// <summary>The acting principal for the current operation. Null when unauthenticated (system jobs, migrations, anonymous requests).</summary>
public interface ICurrentActor
{
    Ulid? Id { get; }
}
