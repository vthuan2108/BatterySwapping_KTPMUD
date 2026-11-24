using BatterySwapping.Contracts;
using BatterySwapping.Core;

namespace BatterySwapping.Server;

public static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/stations/{station}/simulation/reset", (HttpContext context, string station, ResetSimulationRequest request, SqlStore store, ILoggerFactory logs) =>
        {
            var result = new SimulationOperations(store).Reset(station, request);
            var actor = (UserInfo)context.Items["user"]!;
            logs.CreateLogger("Simulation").LogInformation("Simulation reset {ResetId} station {StationId} actor {ActorId}", result.ResetId, station, actor.Id);
            return result;
        }).WithMetadata(new Roles("Admin"));
    }
}
