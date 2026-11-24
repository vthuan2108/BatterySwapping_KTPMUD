using System;
namespace BatterySwapping.Contracts;
public record CreateCustomerRequest(string Username,string Password,string Name,string Phone,string? Email,string LicensePlate,string RequestId);
public record HandoverRequest(int CustomerId,string BatteryId,string Note,string RequestId);
public record CreatePaymentRequest(decimal Amount,string RequestId,int? CustomerId=null);
public record PaymentResultRequest(string Result);
public record PricePolicy(decimal Fee,string SupportedBatteryType);
public record PaymentRequest(string Id,int CustomerId,decimal Amount,string Status,DateTimeOffset CreatedAt,DateTimeOffset? ResolvedAt);
public record BatteryInventoryItem(string BatteryId,string Location,int? CustomerId,string? StationId,int? SlotNumber,bool Reserved);
