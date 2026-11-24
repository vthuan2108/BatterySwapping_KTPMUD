using BatterySwapping.Contracts;
using BatterySwapping.Core;
namespace BatterySwapping.Server;
public static class BusinessEndpoints
{
    static UserInfo User(HttpContext c)=>(UserInfo)c.Items["user"]!;
    public static void MapBusinessEndpoints(this RouteGroupBuilder api)
    {
        var admin=new Roles("Admin");var staff=new Roles("Admin","KyThuatVien");
        api.MapPost("/customers/create",(CreateCustomerRequest r,SqlStore db)=>Results.Ok(new{customerId=new BusinessOperations(db).CreateCustomer(r)})).WithMetadata(admin);
        api.MapGet("/inventory",(SqlStore db)=>new BusinessOperations(db).GetInventory()).WithMetadata(staff);
        api.MapGet("/consistency",(SqlStore db)=>new BusinessOperations(db).Consistency()).WithMetadata(admin);
        api.MapPost("/handovers",(HttpContext c,HandoverRequest r,SqlStore db)=>new BusinessOperations(db).Handover(r,User(c).Id)).WithMetadata(admin);
        api.MapGet("/handovers",(SqlStore db)=>new BusinessOperations(db).Handovers()).WithMetadata(admin);
        api.MapGet("/policy",(SqlStore db)=>new BusinessOperations(db).GetPolicy());
        api.MapPut("/policy",(PricePolicy r,SqlStore db)=>new BusinessOperations(db).SetPolicy(r)).WithMetadata(admin);
        api.MapGet("/payments",(HttpContext c,SqlStore db)=>{var user=User(c);return new BusinessOperations(db).Payments(user.Role=="Admin"?null:user.CustomerId??throw new DomainException("Thiếu hồ sơ khách hàng.",403));}).WithMetadata(new Roles("Admin","KhachHang"));
        api.MapPost("/payments",(HttpContext c,CreatePaymentRequest r,SqlStore db)=>{var user=User(c);int customer=user.Role=="Admin"?r.CustomerId??throw new DomainException("Chọn khách hàng.",400):user.CustomerId??throw new DomainException("Thiếu hồ sơ khách hàng.",403);if(user.Role!="Admin"&&r.CustomerId.HasValue&&r.CustomerId!=customer)throw new DomainException("Không được nạp ví người khác.",403);var p=new BusinessOperations(db).CreatePayment(customer,r);return Results.Accepted($"/api/v1/payments",p);}).WithMetadata(new Roles("Admin","KhachHang"));
        api.MapPost("/payments/{id}/result",(HttpContext c,string id,PaymentResultRequest r,SqlStore db,ILoggerFactory logs)=>{var p=new BusinessOperations(db).ResolvePayment(id,r.Result,User(c).Id);logs.CreateLogger("Payments").LogInformation("Payment {PaymentId} {Status} actor {ActorId}",p.Id,p.Status,User(c).Id);return p;}).WithMetadata(admin);
    }
}
