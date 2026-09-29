using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kestrel.Api.Hubs;

/// <summary>
/// Real-time event push skeleton (live tail arrives in Phase 11).
/// The hub is authenticated — anonymous connections are refused by the
/// authorization middleware; per-method role policies arrive with Phase 11.
/// </summary>
[Authorize]
public class EventsHub : Hub
{
    public async Task Subscribe(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new HubException("Channel name is required.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, channel.Trim());
    }

    public async Task Unsubscribe(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new HubException("Channel name is required.");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel.Trim());
    }
}

/// <summary>
/// Alert push skeleton (alerting arrives in Phase 5/11). Also authenticated.
/// </summary>
[Authorize]
public class AlertsHub : Hub
{
    public async Task Acknowledge(string alertId)
    {
        if (string.IsNullOrWhiteSpace(alertId))
        {
            throw new HubException("Alert id is required.");
        }

        await Clients.Group(Context.ConnectionId).SendAsync("AlertAcknowledged", alertId.Trim());
    }
}

