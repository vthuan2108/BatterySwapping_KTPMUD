namespace BatterySwapping.Contracts;
public record SlotCooldown(string StationId,int SlotNumber,string? BatteryId,DateTimeOffset StartedAt,DateTimeOffset UnlockAfter,DateTimeOffset? ClearedAt);
