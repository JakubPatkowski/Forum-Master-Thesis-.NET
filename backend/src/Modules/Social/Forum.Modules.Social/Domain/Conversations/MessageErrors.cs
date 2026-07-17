using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Domain.Conversations;

internal static class MessageErrors
{
    public static readonly Error Deleted =
        Error.Validation("Message.Deleted", "This message has been deleted.");
}
