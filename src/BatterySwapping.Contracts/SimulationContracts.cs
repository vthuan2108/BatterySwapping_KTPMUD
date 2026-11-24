namespace BatterySwapping.Contracts;

public record ResetSimulationRequest(string RequestId, string ClientId, string LeaseId);
public record SimulationResetResult(string ResetId, string StationId, string LeaseId);
