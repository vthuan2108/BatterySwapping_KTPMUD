using BatterySwapping.Contracts;
namespace BatterySwapping.Core;
public sealed class CustomerWallet(SqlStore store)
{
    public decimal TopUp(int customer,decimal amount,string key) => store.Write(db=>{
        if(amount<=0||amount>10000000||decimal.Round(amount,0)!=amount||!Guid.TryParse(key,out _))throw new DomainException("Nhập số tiền nguyên từ 1 đến 10.000.000 đ và mã yêu cầu hợp lệ.",400);
        var previous=db.Rows("SELECT CustomerId,Amount FROM dbo.BssTopups WHERE RequestId=@id",("@id",key)).SingleOrDefault();
        if(previous!=null){if((int)previous["CustomerId"]! !=customer||(decimal)previous["Amount"]! !=amount)throw new DomainException("Mã yêu cầu đã được sử dụng.");}
        else
        {
            if(db.Exec("UPDATE dbo.KhachHang SET SoDuVi=SoDuVi+@amount WHERE MaKH=@c",("@amount",amount),("@c",customer))!=1)throw new DomainException("Không tìm thấy ví khách hàng.",404);
            db.Exec("INSERT dbo.BssTopups(RequestId,CustomerId,Amount) VALUES(@id,@c,@amount); INSERT dbo.LichSuNapTien(MaKH,SoTien,PhuongThuc,MaGiaoDichNganHang,TrangThai) VALUES(@c,@amount,N'DemoTuNap',@id,N'ThanhCong')",("@id",key),("@c",customer),("@amount",amount));
        }
        return Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer)));
    });
}
