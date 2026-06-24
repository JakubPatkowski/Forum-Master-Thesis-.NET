using Forum.Common.Security;

namespace Forum.Infrastructure.Persistence;

/// <summary>Default actor used until the Identity module supplies a real one (Phase 1). Represents "no authenticated user".</summary>
internal sealed class UnauthenticatedActor : ICurrentActor
{
    public Ulid? Id => null;
}
