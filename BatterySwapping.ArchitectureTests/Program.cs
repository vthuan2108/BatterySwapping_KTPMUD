using BatterySwapping.Core;

var hash = Passwords.Hash("123456");
Check(hash != Passwords.Hash("123456"), "salt is random");
Check(Passwords.Verify("123456", hash), "PBKDF2 roundtrip");
Check(!Passwords.Verify(hash, hash), "hash is not a password");
Check(!Passwords.Verify("bad", hash), "wrong password denied");

if(args.Contains("--wallet"))
{
    // Explicit dedicated fixture; never use the running application's database.
    var store=new SqlStore(@"Server=.\SQLEXPRESS;Database=BssArchitectureTests_20261121;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;");
    store.Migrate(File.ReadAllText("Database/06_Runtime.sql"));
    store.Migrate(File.ReadAllText("Database/06_Runtime.sql"));
    var wallet=new CustomerWallet(store);
    int customer=store.Read(d=>Convert.ToInt32(d.Scalar("SELECT TOP 1 MaKH FROM dbo.KhachHang ORDER BY MaKH")));
    decimal before=store.Read(d=>Convert.ToDecimal(d.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer))));
    string key=Guid.NewGuid().ToString();
    Check(wallet.TopUp(customer,50000,key)==before+50000,"demo wallet credits requested amount");
    Check(wallet.TopUp(customer,50000,key)==before+50000,"retry cannot credit wallet twice");
    try{wallet.TopUp(customer,60000,key);throw new Exception("duplicate key accepted with different amount");}catch(DomainException){Check(true,"changed amount cannot reuse request key");}
    try{wallet.TopUp(customer,-1,Guid.NewGuid().ToString());throw new Exception("negative amount accepted");}catch(DomainException){Check(true,"negative topup rejected");}
    Check(store.Read(d=>Convert.ToInt32(d.Scalar("SELECT COUNT(*) FROM dbo.LichSuNapTien WHERE MaGiaoDichNganHang=@id",("@id",key))))==1,"single persistent topup history entry");
}
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
