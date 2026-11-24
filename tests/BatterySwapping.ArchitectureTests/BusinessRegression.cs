using BatterySwapping.Contracts;
using BatterySwapping.Core;

internal static class BusinessRegression
{
    public static void Run()
    {
        var store=new SqlStore(@"Server=.\SQLEXPRESS;Database=BssArchitectureTests_20261121;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;");
        store.Migrate(File.ReadAllText("Database/06_Runtime.sql"));
        store.Migrate(File.ReadAllText("Database/07_Business.sql"));
        store.Migrate(File.ReadAllText("Database/07_Business.sql"));
        var business=new BusinessOperations(store);
        string suffix=Guid.NewGuid().ToString("N")[..10];
        var customerRequest=new CreateCustomerRequest("test_"+suffix,"testpass123","Business regression","09"+DateTime.UtcNow.Ticks.ToString()[^12..],null,"TEST-"+suffix,Guid.NewGuid().ToString());
        int customer=business.CreateCustomer(customerRequest);
        Check(business.CreateCustomer(customerRequest)==customer,"customer retry returns same profile");
        Denied(()=>business.CreateCustomer(customerRequest with{Password="changedpass123"}),"customer retry rejects changed password");
        Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer))))==0,"customer wallet starts zero");
        Check(store.Read(db=>db.Scalar("SELECT MaPinDangMuon FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer)))==null,"customer has no invented battery");
        string paymentKey=Guid.NewGuid().ToString();var payment=business.CreatePayment(customer,new(50000,paymentKey));
        Check(payment.Status=="Pending"&&store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer))))==0,"pending payment does not credit wallet");
        Check(business.CreatePayment(customer,new(50000,paymentKey)).Id==payment.Id,"payment creation is idempotent");
        business.ResolvePayment(payment.Id,"Succeeded",1);business.ResolvePayment(payment.Id,"Succeeded",1);
        Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer))))==50000,"payment repeated success credits exactly once");
        Denied(()=>business.ResolvePayment(payment.Id,"Failed",1),"final payment result immutable");
        var failed=business.CreatePayment(customer,new(70000,Guid.NewGuid().ToString()));business.ResolvePayment(failed.Id,"Failed",1);Denied(()=>business.ResolvePayment(failed.Id,"Succeeded",1),"failed payment cannot become success");
        string battery="BT-"+suffix;
        store.Write(db=>db.Exec("INSERT dbo.Pin(MaPin,LoaiPin) VALUES(@p,@t)",("@p",battery),("@t",business.GetPolicy().SupportedBatteryType)));
        var handover=new HandoverRequest(customer,battery,"Manual warehouse receipt",Guid.NewGuid().ToString());business.Handover(handover,1);business.Handover(handover,1);
        Check(business.GetInventory().Single(i=>i.BatteryId==battery).Location=="Customer","handover updates inventory owner");
        int other=business.CreateCustomer(customerRequest with{Username="other_"+suffix,Phone="08"+DateTime.UtcNow.Ticks.ToString()[^12..],RequestId=Guid.NewGuid().ToString()});
        Denied(()=>business.Handover(new(other,battery,"Must reject already owned pin",Guid.NewGuid().ToString()),1),"same battery cannot be handed to another customer");
        Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssHandovers WHERE Id=@id",("@id",handover.RequestId))))==1,"handover audit is idempotent");
        string competing="BC-"+suffix;
        store.Write(db=>db.Exec("INSERT dbo.Pin(MaPin,LoaiPin) VALUES(@p,@t)",("@p",competing),("@t",business.GetPolicy().SupportedBatteryType)));
        int third=business.CreateCustomer(customerRequest with{Username="third_"+suffix,Phone="07"+DateTime.UtcNow.Ticks.ToString()[^12..],RequestId=Guid.NewGuid().ToString()});
        int wins=0;Parallel.ForEach(new[]{other,third},c=>{try{business.Handover(new(c,competing,"Concurrent warehouse assignment",Guid.NewGuid().ToString()),1);Interlocked.Increment(ref wins);}catch(DomainException){}});
        Check(wins==1,"concurrent handovers have exactly one winner");
        string? cabinet=store.Read(db=>db.Scalar("SELECT TOP 1 MaPinHienTai FROM dbo.NganTu WHERE MaPinHienTai IS NOT NULL") as string);
        if(cabinet!=null)Denied(()=>store.Write(db=>{BusinessOperations.EnsureWarehouse(db,cabinet);return true;}),"cabinet battery cannot be assigned as warehouse stock");
        var cancelled=business.CreatePayment(customer,new(20000,Guid.NewGuid().ToString()));business.ResolvePayment(cancelled.Id,"Cancelled",1);Denied(()=>business.ResolvePayment(cancelled.Id,"Succeeded",1),"cancelled payment cannot credit wallet");
        Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer))))==50000,"failed and cancelled payments leave wallet unchanged");
        Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.LichSuNapTien WHERE MaGiaoDichNganHang=@id",("@id",payment.Id))))==1,"single payment ledger entry");
    }
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
    static void Denied(Action action,string name){try{action();}catch(DomainException){Check(true,name);return;}throw new Exception("FAIL "+name);}
}
