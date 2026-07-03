namespace Forum.Infrastructure.Storage;

/// <summary>
/// Metadata of a stored object as reported by the store itself. <see cref="SizeBytes"/> is authoritative
/// (actual stored bytes); <see cref="ContentType"/> is what the uploader declared on the PUT and therefore
/// still untrusted — callers must sniff the real type from the object bytes.
/// </summary>
public sealed record ObjectStatResult(long SizeBytes, string ContentType, string ETag);
