using Forum.Api.Realtime;
using Forum.Common.Security;

namespace Forum.Api.Extensions;

/// <summary>
/// Phase 7 wiring: the WebSocket hub (ADR 0010). Two endpoints — an authenticated REST call minting a
/// short-lived single-use connect ticket, and the WebSocket handshake that redeems it — plus the background
/// change-feed consumer that fans integration events out to this replica's sockets.
/// </summary>
public static class RealtimeExtensions
{
    public static IServiceCollection AddForumRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RealtimeOptions>(configuration.GetSection(RealtimeOptions.SectionName));

        services.AddSingleton<RealtimeTicketService>();
        services.AddSingleton<RealtimeConnectionRegistry>();
        services.AddSingleton<RealtimeSocketHandler>();
        services.AddSingleton<IRealtimeNotificationSink, RealtimeNotificationDispatcher>();
        services.AddHostedService<RealtimeChangeFeedService>();

        return services;
    }

    public static WebApplication MapForumRealtime(this WebApplication app)
    {
        app.UseWebSockets();

        // The bearer-authenticated half of the handshake: trade the in-memory access token for a connect ticket.
        app.MapPost("/api/realtime/ticket", static (ICurrentUser currentUser, RealtimeTicketService tickets) =>
            {
                if (currentUser.Id is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var (ticket, expiresInSeconds) = tickets.Issue(userId);
                return Results.Ok(new { ticket, expiresInSeconds });
            })
            .RequireAuthorization()
            .WithName("CreateRealtimeTicket")
            .WithTags("Realtime")
            .WithSummary("Mints a short-lived, single-use ticket that authenticates the WebSocket handshake.");

        // The socket itself authenticates via ?ticket= (redeemed exactly once), never via Authorization header.
        app.MapGet("/api/realtime/ws", static async (
                HttpContext context, RealtimeTicketService tickets, RealtimeSocketHandler handler) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    return Results.BadRequest();
                }

                if (!tickets.TryRedeem(context.Request.Query["ticket"], out var userId))
                {
                    return Results.Unauthorized();
                }

                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await handler.HandleAsync(socket, userId, context.RequestAborted);
                return Results.Empty;
            })
            .WithName("RealtimeSocket")
            .WithTags("Realtime")
            .WithSummary("WebSocket handshake (requires a fresh ticket). Speaks the subscribe/unsubscribe protocol "
                + "and pushes compact change notifications per ADR 0010.");

        return app;
    }
}
