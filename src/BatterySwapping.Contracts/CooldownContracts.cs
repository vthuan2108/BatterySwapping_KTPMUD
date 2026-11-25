namespace BatterySwapping.Contracts;
public record SlotCooldown(string StationId,int SlotNumber,string? BatteryId,DateTimeOffset StartedAt,DateTimeOffset UnlockAfter,DateTimeOffset? ClearedAt,int IncidentCount=1,bool RequiresAdmin=false);
public record BatteryThermalState(string BatteryId,int IncidentCount,int TotalIncidents,bool RequiresAdmin,DateTimeOffset? LastAdminResetAt);
