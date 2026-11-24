using BatterySwapping.Contracts;
using BatterySwapping.Core;
namespace BatterySwapping.Server;
public static class MaintenanceEndpoints
{
 public static void MapMaintenanceEndpoints(this RouteGroupBuilder api){
  var staff=new Roles("Admin","KyThuatVien");
  api.MapPost("/maintenance/{id}/retry",(string id,HttpContext c,BssEngine e)=>Results.Accepted("/api/v1/maintenance",e.RetryMaintenance(id,((UserInfo)c.Items["user"]!).Role=="Admin"))).WithMetadata(staff);
  api.MapGet("/maintenance",(BssEngine e)=>e.MaintenanceOrders()).WithMetadata(staff);
  api.MapPost("/maintenance",(HttpContext c,MaintenanceOrderRequest r,BssEngine e,ILoggerFactory logs)=>{var order=e.CreateMaintenance(r,((UserInfo)c.Items["user"]!).Id,((UserInfo)c.Items["user"]!).Role=="Admin");logs.CreateLogger("Maintenance").LogInformation("Maintenance {MaintenanceId} command {CommandId} actor {ActorId}",order.Id,order.CommandId,order.ActorId);return Results.Accepted($"/api/v1/maintenance",order);}).WithMetadata(staff);
  api.MapPost("/swaps/{id}/return-old",(string id,BssEngine e)=>Results.Accepted($"/api/v1/swaps/{id}",e.ReturnOldBattery(id))).WithMetadata(staff);
 }
}
