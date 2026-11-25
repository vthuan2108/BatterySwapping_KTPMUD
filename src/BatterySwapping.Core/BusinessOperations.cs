using System.Text.Json;
using BatterySwapping.Contracts;
namespace BatterySwapping.Core;

public sealed class BusinessOperations(SqlStore store)
{
    static void Require(bool ok,string error,int status=409){if(!ok)throw new DomainException(error,status);}
    static string Key(string value){Require(Guid.TryParse(value,out _),"Mã yêu cầu phải là UUID.",400);return Guid.Parse(value).ToString();}
    public static PricePolicy Policy(Db db){var row=db.Rows("SELECT Fee,SupportedBatteryType FROM dbo.BssPolicy WHERE Id=1").Single();return new((decimal)row["Fee"]!,(string)row["SupportedBatteryType"]!);}
    public PricePolicy GetPolicy()=>store.Read(Policy);
    public PricePolicy SetPolicy(PricePolicy p)=>store.Write(db=>{Require(p.Fee>=0&&p.Fee<=10000000&&decimal.Truncate(p.Fee)==p.Fee,"Phí phải là số tiền nguyên từ 0 đến 10.000.000 đ.",400);Require(!string.IsNullOrWhiteSpace(p.SupportedBatteryType)&&p.SupportedBatteryType.Length<=50,"Loại pin không hợp lệ.",400);db.Exec("UPDATE dbo.BssPolicy SET Fee=@f,SupportedBatteryType=@t WHERE Id=1",("@f",p.Fee),("@t",p.SupportedBatteryType.Trim()));return Policy(db);});
    static T Once<T>(Db db,string key,string kind,object request,Func<T> action){string payload=JsonSerializer.Serialize(request,SqlStore.Json);var previous=db.Rows("SELECT Kind,Payload,Result FROM dbo.BssBusinessRequests WHERE Id=@id",("@id",key)).SingleOrDefault();if(previous!=null){Require((string)previous["Kind"]! ==kind&&(string)previous["Payload"]! ==payload,"Mã yêu cầu đã được sử dụng với nội dung khác.");return JsonSerializer.Deserialize<T>((string)previous["Result"]!,SqlStore.Json)!;}T result=action();db.Exec("INSERT dbo.BssBusinessRequests VALUES(@id,@kind,@p,@r)",("@id",key),("@kind",kind),("@p",payload),("@r",JsonSerializer.Serialize(result,SqlStore.Json)));return result;}
    public int CreateCustomer(CreateCustomerRequest r)=>store.Write(db=>{
        Require(!string.IsNullOrWhiteSpace(r.Password)&&r.Password.Length>=6&&r.Password.Length<=128,"Mật khẩu từ 6 đến 128 ký tự.",400);
        string key=Key(r.RequestId);string? previousPayload=db.Scalar("SELECT Payload FROM dbo.BssBusinessRequests WHERE Id=@id AND Kind='Customer'",("@id",key)) as string;
        string passwordHash=previousPayload==null?Passwords.Hash(r.Password!):JsonDocument.Parse(previousPayload).RootElement.GetProperty("passwordHash").GetString()!;
        Require(Passwords.Verify(r.Password!,passwordHash),"Mã yêu cầu đã được sử dụng với mật khẩu khác.");
        return Once(db,key,"Customer",new {r.Username,PasswordHash=passwordHash,r.Name,r.Phone,r.Email,r.LicensePlate,r.RequestId},()=>{
        Require(!string.IsNullOrWhiteSpace(r.Username)&&r.Username.Length<=50&&r.Username.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'||c=='-'||c=='.'),"Tên đăng nhập không hợp lệ.",400);
        Require(!string.IsNullOrWhiteSpace(r.Password)&&r.Password.Length>=6&&r.Password.Length<=128,"Mật khẩu từ 6 đến 128 ký tự.",400);
        Require(!string.IsNullOrWhiteSpace(r.Name)&&r.Name.Length<=100&&!string.IsNullOrWhiteSpace(r.Phone)&&r.Phone.Length<=20&&r.Phone.All(c=>char.IsDigit(c)||c=='+'),"Họ tên hoặc số điện thoại không hợp lệ.",400);
        Require(!string.IsNullOrWhiteSpace(r.LicensePlate)&&r.LicensePlate.Length<=20&&(r.Email?.Length??0)<=100,"Biển số hoặc email không hợp lệ.",400);
        Require(db.Scalar("SELECT MaTK FROM dbo.TaiKhoan WHERE TenDangNhap=@u",("@u",r.Username))==null&&db.Scalar("SELECT MaKH FROM dbo.KhachHang WHERE SoDienThoai=@p",("@p",r.Phone))==null,"Tên đăng nhập hoặc số điện thoại đã tồn tại.");
        int account=Convert.ToInt32(db.Scalar("INSERT dbo.TaiKhoan(TenDangNhap,MatKhau,HoTen,VaiTro,TrangThai) VALUES(@u,@p,@n,N'KhachHang',N'HoatDong'); SELECT CAST(SCOPE_IDENTITY() AS int)",("@u",r.Username),("@p",passwordHash),("@n",r.Name)));
        return Convert.ToInt32(db.Scalar("INSERT dbo.KhachHang(MaTK,HoTen,SoDienThoai,Email,BienSoXe,SoDuVi,MaPinDangMuon) VALUES(@a,@n,@p,@e,@l,0,NULL); SELECT CAST(SCOPE_IDENTITY() AS int)",("@a",account),("@n",r.Name),("@p",r.Phone),("@e",r.Email),("@l",r.LicensePlate)));
    });
    });
    static bool MaintenanceReserved(Db db,string battery)=>db.Scalar("SELECT OBJECT_ID('dbo.BssMaintenanceLocks')")!=null&&db.Scalar("SELECT MaintenanceId FROM dbo.BssMaintenanceLocks WHERE Resource=@r",("@r","pin:"+battery))!=null;
    public static List<BatteryInventoryItem> Inventory(Db db)=>db.Rows("SELECT p.MaPin,p.TrangThai,p.SoH,p.NhietDo,l.Location,(SELECT COUNT(*) FROM dbo.KhachHang k WHERE k.MaPinDangMuon=p.MaPin) Owners,(SELECT MIN(MaKH) FROM dbo.KhachHang k WHERE k.MaPinDangMuon=p.MaPin) CustomerId,n.MaTram,n.SoThuTu,CASE WHEN EXISTS(SELECT 1 FROM dbo.BssReservations r WHERE r.Resource='pin:'+p.MaPin) THEN 1 ELSE 0 END Reserved FROM dbo.Pin p LEFT JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin LEFT JOIN dbo.BssBatteryLocations l ON l.BatteryId=p.MaPin").Select(row=>{
        int owners=(int)row["Owners"]!;string? station=row["MaTram"] as string;string state=(string)row["TrangThai"]!;string location=owners>1||owners>0&&station!=null?"Conflict":station!=null?"Cabinet":owners==1?"Customer":row["Location"] as string??(state is not ("DaDay" or "KhaDung" or "DangSac")||(decimal)row["SoH"]!<80||(decimal)row["NhietDo"]!>=45?"Quarantine":"Warehouse");
        return new BatteryInventoryItem((string)row["MaPin"]!,location,row["CustomerId"] as int?,station,row["SoThuTu"] as int?,(int)row["Reserved"]! ==1||MaintenanceReserved(db,(string)row["MaPin"]!));
    }).ToList();
    public List<BatteryInventoryItem> GetInventory()=>store.Read(Inventory);
    public static void EnsureWarehouse(Db db,string battery){var item=Inventory(db).Find(i=>i.BatteryId==battery);Require(item!=null,"Không tìm thấy pin.",404);Require(item!.Location=="Warehouse"&&!item.Reserved,"Pin không sẵn sàng trong kho.");var p=db.Rows("SELECT * FROM dbo.Pin WHERE MaPin=@p",("@p",battery)).Single();Require((string)p["LoaiPin"]! == Policy(db).SupportedBatteryType,"Pin khác loại được hỗ trợ.");Require((decimal)p["SoH"]!>=80&&(decimal)p["SoC"]!>=90&&(decimal)p["NhietDo"]!<=45&&((string)p["TrangThai"]!) is "DaDay" or "KhaDung" or "DangSac","Pin không đạt điều kiện bàn giao.");}
    public List<Dictionary<string,object?>> Consistency()=>store.Read(db=>db.Rows("SELECT 'DuplicateOwner' Issue,MaPinDangMuon BatteryId,CAST(NULL AS int) CustomerId FROM dbo.KhachHang WHERE MaPinDangMuon IS NOT NULL GROUP BY MaPinDangMuon HAVING COUNT(*)>1 UNION ALL SELECT 'CustomerAndCabinet',k.MaPinDangMuon,k.MaKH FROM dbo.KhachHang k JOIN dbo.NganTu n ON n.MaPinHienTai=k.MaPinDangMuon UNION ALL SELECT 'MissingBattery',k.MaPinDangMuon,k.MaKH FROM dbo.KhachHang k LEFT JOIN dbo.Pin p ON p.MaPin=k.MaPinDangMuon WHERE k.MaPinDangMuon IS NOT NULL AND p.MaPin IS NULL UNION ALL SELECT 'MissingCustomerProfile',NULL,t.MaTK FROM dbo.TaiKhoan t LEFT JOIN dbo.KhachHang k ON k.MaTK=t.MaTK WHERE t.VaiTro=N'KhachHang' AND k.MaKH IS NULL UNION ALL SELECT 'MissingAccount',k.MaPinDangMuon,k.MaKH FROM dbo.KhachHang k LEFT JOIN dbo.TaiKhoan t ON t.MaTK=k.MaTK WHERE t.MaTK IS NULL"));
    public Dictionary<string,object?> Handover(HandoverRequest r,int actor)=>store.Write(db=>Once(db,Key(r.RequestId),"Handover",r,()=>{
        Require(!string.IsNullOrWhiteSpace(r.Note)&&r.Note.Length<=500,"Nhập ghi chú bàn giao (tối đa 500 ký tự).",400);
        var customer=db.Rows("SELECT k.* FROM dbo.KhachHang k JOIN dbo.TaiKhoan t ON t.MaTK=k.MaTK WHERE k.MaKH=@c AND t.VaiTro=N'KhachHang' AND t.TrangThai=N'HoatDong'",("@c",r.CustomerId)).SingleOrDefault();Require(customer!=null,"Khách hàng không tồn tại hoặc tài khoản không hoạt động.",404);Require(customer!["MaPinDangMuon"]==null,"Khách hàng đã giữ pin.");Require(db.Scalar("SELECT Id FROM dbo.BssSessions WHERE CustomerId=@c AND Active=1",("@c",r.CustomerId))==null,"Khách hàng có phiên đang hoạt động.");EnsureWarehouse(db,r.BatteryId);
        db.Exec("UPDATE dbo.KhachHang SET MaPinDangMuon=@p WHERE MaKH=@c; UPDATE dbo.Pin SET TrangThai=N'DangSuDung',NgayCapNhatCuoi=GETDATE() WHERE MaPin=@p; DELETE dbo.BssBatteryLocations WHERE BatteryId=@p; INSERT dbo.BssHandovers(Id,CustomerId,BatteryId,ActorId,Note) VALUES(@id,@c,@p,@a,@n)",("@p",r.BatteryId),("@c",r.CustomerId),("@id",Key(r.RequestId)),("@a",actor),("@n",r.Note));
        return db.Rows("SELECT * FROM dbo.BssHandovers WHERE Id=@id",("@id",Key(r.RequestId))).Single();
    }));
    public List<Dictionary<string,object?>> Handovers()=>store.Read(db=>db.Rows("SELECT * FROM dbo.BssHandovers ORDER BY CreatedAt DESC"));
    static readonly HashSet<string> AllowedPaymentMethods=new(StringComparer.Ordinal){"CongGiaLap","ChuyenKhoan","TienMat","TheNganHang"};
    static PaymentRequest Payment(Dictionary<string,object?> r)=>new((string)r["Id"]!,(int)r["CustomerId"]!,(decimal)r["Amount"]!,(string)r["Status"]!,(DateTimeOffset)r["CreatedAt"]!,r["ResolvedAt"] as DateTimeOffset?,r.TryGetValue("Method",out var m)&&m is string ms?ms:"CongGiaLap");
    public List<PaymentRequest> Payments(int? customer)=>store.Read(db=>db.Rows("SELECT * FROM dbo.BssPayments WHERE @c IS NULL OR CustomerId=@c ORDER BY CreatedAt DESC",("@c",customer)).Select(Payment).ToList());
    public PaymentRequest CreatePayment(int customer,CreatePaymentRequest r)=>store.Write(db=>{
        string key=Key(r.RequestId);Require(r.Amount>0&&r.Amount<=10000000&&decimal.Truncate(r.Amount)==r.Amount,"Số tiền nguyên từ 1 đến 10.000.000 đ.",400);
        string method=string.IsNullOrWhiteSpace(r.Method)?"CongGiaLap":r.Method.Trim();
        Require(method.Length<=50&&AllowedPaymentMethods.Contains(method),"Phương thức thanh toán không hợp lệ.",400);
        var old=db.Rows("SELECT * FROM dbo.BssPayments WHERE Id=@id",("@id",key)).SingleOrDefault();if(old!=null){Require((int)old["CustomerId"]! ==customer&&(decimal)old["Amount"]! ==r.Amount&&string.Equals(old.TryGetValue("Method",out var om)?om as string??"CongGiaLap":"CongGiaLap",method,StringComparison.Ordinal),"Mã yêu cầu đã được sử dụng.");return Payment(old);}
        Require(db.Scalar("SELECT MaKH FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer))!=null,"Không tìm thấy khách hàng.",404);
        db.Exec("INSERT dbo.BssPayments(Id,CustomerId,Amount,Status,Method) VALUES(@id,@c,@a,'Pending',@m)",("@id",key),("@c",customer),("@a",r.Amount),("@m",method));return Payment(db.Rows("SELECT * FROM dbo.BssPayments WHERE Id=@id",("@id",key)).Single());
    });
    public PaymentRequest ResolvePayment(string id,string result,int actor)=>store.Write(db=>{
        Require(result is "Succeeded" or "Failed" or "Cancelled","Kết quả thanh toán không hợp lệ.",400);var row=db.Rows("SELECT * FROM dbo.BssPayments WHERE Id=@id",("@id",id)).SingleOrDefault();Require(row!=null,"Không tìm thấy yêu cầu nạp.",404);var p=Payment(row!);
        if(p.Status!="Pending"){Require(p.Status==result,"Không được thay đổi kết quả thanh toán đã chốt.");return p;}
        if(result=="Succeeded")db.Exec("UPDATE dbo.KhachHang SET SoDuVi=SoDuVi+@a WHERE MaKH=@c; INSERT dbo.LichSuNapTien(MaKH,SoTien,PhuongThuc,MaGiaoDichNganHang,TrangThai) VALUES(@c,@a,@m,@id,N'ThanhCong')",("@a",p.Amount),("@c",p.CustomerId),("@m",p.Method),("@id",p.Id));
        db.Exec("UPDATE dbo.BssPayments SET Status=@s,ResolvedAt=SYSDATETIMEOFFSET(),ResolvedBy=@actor WHERE Id=@id",("@s",result),("@actor",actor),("@id",p.Id));return Payment(db.Rows("SELECT * FROM dbo.BssPayments WHERE Id=@id",("@id",p.Id)).Single());
    });
}
