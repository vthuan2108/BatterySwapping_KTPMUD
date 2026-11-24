namespace BatterySwapping.Contracts;
public record MaintenanceOrderRequest(string StationId,int SlotNumber,string Action,string? NewBatteryId,string Note,string RequestId);
public record MaintenanceOrder(string Id,string CommandId,string Status,string StationId,int SlotNumber,string Action,string? OldBatteryId,string? NewBatteryId,string Note,int ActorId,DateTimeOffset CreatedAt,string? Result);
