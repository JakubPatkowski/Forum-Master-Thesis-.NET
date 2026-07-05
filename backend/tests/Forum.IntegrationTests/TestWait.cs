using Xunit.Sdk;

namespace Forum.IntegrationTests;

/// <summary>Polls an async condition until it holds — how tests observe the asynchronous outbox → consumer pipeline.</summary>
internal static class TestWait
{
    public static async Task UntilAsync(Func<Task<bool>> condition, string because, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(150);
        }

        throw new XunitException($"Condition not met within {timeoutSeconds}s: {because}.");
    }
}
