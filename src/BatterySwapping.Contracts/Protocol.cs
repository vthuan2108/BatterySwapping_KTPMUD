namespace BatterySwapping.Contracts;

public record LoginRequest(string Username, string Password);
public record UserInfo(int Id, string Username, string Name, string Role, int? CustomerId);
public record LoginResponse(string Token, UserInfo User);
public record StartSwap(string StationId, string IdempotencyKey);
public record DeviceLease(string StationId, string ClientId);
public record LeaseResponse(string LeaseId, string StationId, DateTimeOffset ExpiresAt);
public record MoneyRequest(int CustomerId, decimal Amount, string Method);
public record SelfTopupRequest(decimal Amount,string RequestId);
public record MaintenanceRequest(string? NewBatteryId, string Note);
public record CommandRequest(string Command, int? SlotNumber = null, string? BatteryId = null);
public record KioskAuthRequest(string Identifier);
public record SwapEventRequest(string Event);
public record ErrorResponse(string Error, string Code);
public class SwapSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Key { get; set; } = "";
    public int CustomerId { get; set; }
    public string StationId { get; set; } = "";
    public string OldBatteryId { get; set; } = "";
    public string NewBatteryId { get; set; } = "";
    public int ReturnSlot { get; set; }
    public int ReceiveSlot { get; set; }
    public string State { get; set; } = "AwaitingOldBattery";
    public decimal Fee { get; set; } = 25000;
    public bool Charged { get; set; }
    public bool OldInserted { get; set; }
    public int? TransactionId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(2);
}
public class DeviceCommand
{
    public int SchemaVersion { get; set; } = 1;
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
    public string StationId { get; set; } = "";
    public string Command { get; set; } = "";
    public int? SlotNumber { get; set; }
    public string? BatteryId { get; set; }
    public string? SessionId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ThermalStartedAt { get; set; }
    public bool AdminThermalReset { get; set; }
    public bool PreserveStationLock { get; set; }
}
public record SlotTelemetry(int SlotNumber, string? BatteryId, decimal Soc, decimal Soh,
    decimal Temperature, decimal Voltage, decimal Current, bool IsCharging, bool IsDoorOpen, bool IsLocked, bool IsOverheated = false);
public record DeviceMessage(int SchemaVersion, string StationId, string LeaseId, string MessageId,
    long Sequence, DateTimeOffset Timestamp, string? Event, string? CommandId, string? SessionId,
    int? SlotNumber, string? BatteryId, bool Success, SlotTelemetry[] Slots);
public record StationStatus(string StationId, bool Online, DateTimeOffset? LastSeen, string? OwnerId);
