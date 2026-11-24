using System.Text.Json;
using BatterySwapping.Contracts;

namespace BatterySwapping.Core;

public sealed class SimulationOperations(SqlStore store)
{
    static void Require(bool condition, string message, int status = 409)
    {
        if (!condition) throw new DomainException(message, status);
    }

    public SimulationResetResult Reset(string station, ResetSimulationRequest request) => store.Write(db =>
    {
        Require(Guid.TryParse(request.RequestId, out _), "Mã yêu cầu phải là UUID.", 400);
        Require(!string.IsNullOrWhiteSpace(request.ClientId) && request.ClientId.Length <= 64 && !string.IsNullOrWhiteSpace(request.LeaseId), "Thiếu thông tin simulator.", 400);
        string key = Guid.Parse(request.RequestId).ToString();
        string payload = JsonSerializer.Serialize(new { StationId = station, request.ClientId, request.LeaseId }, SqlStore.Json);
        var device = db.Rows("SELECT * FROM dbo.BssDevices WHERE StationId=@s", ("@s", station)).SingleOrDefault();
        Require(device != null && (string)device["ClientId"]! == request.ClientId, "Simulator không còn quyền điều khiển.");

        // Resolve a lost response before checking the old lease: reset itself rotated it.
        var previous = db.Rows("SELECT Kind,Payload,Result FROM dbo.BssBusinessRequests WHERE Id=@id", ("@id", key)).SingleOrDefault();
        if (previous != null)
        {
            Require((string)previous["Kind"]! == "SimulationReset" && (string)previous["Payload"]! == payload, "Mã yêu cầu đã được sử dụng với nội dung khác.");
            var result = JsonSerializer.Deserialize<SimulationResetResult>((string)previous["Result"]!, SqlStore.Json)!;
            Require((string)device!["LeaseId"]! == result.LeaseId, "Quyền điều khiển simulator đã thay đổi.");
            return result;
        }

        // Reset pauses telemetry while its outcome is uncertain. The current client and
        // lease still prove ownership after an idle period unless another lease took over.
        Require((string)device!["LeaseId"]! == request.LeaseId, "Simulator không còn quyền điều khiển hoặc lease không hợp lệ.");
        var stationState = db.Scalar("SELECT TrangThaiHoatDong FROM dbo.TramPin WHERE MaTram=@s", ("@s", station)) as string;
        Require(stationState != null, "Không tìm thấy trạm.", 404);
        bool isolated = stationState != "HoatDong";
        Require(db.Scalar("SELECT TOP 1 Id FROM dbo.BssSessions WHERE StationId=@s AND Active=1", ("@s", station)) == null, "Trạm còn phiên đổi pin đang hoạt động.");
        Require(db.Scalar("SELECT TOP 1 l.Resource FROM dbo.BssMaintenanceLocks l JOIN dbo.BssMaintenance m ON m.Id=l.MaintenanceId WHERE m.StationId=@s OR l.Resource LIKE 'slot:'+@s+':%' OR l.Resource IN(SELECT 'pin:'+MaPinHienTai FROM dbo.NganTu WHERE MaTram=@s)", ("@s", station)) == null, "Trạm còn phiếu bảo trì chưa kết thúc.");
        Require(db.Scalar("SELECT TOP 1 Id FROM dbo.BssCommands WHERE StationId=@s AND Status IN('Pending','Sent','Unconfirmed') AND ISNULL(JSON_VALUE(Payload,'$.command'),'')<>'QUERY_STATUS'", ("@s", station)) == null, "Trạm còn lệnh điều khiển chưa đối soát.");
        Require(db.Scalar("SELECT TOP 1 k.MaKH FROM dbo.KhachHang k JOIN dbo.NganTu n ON n.MaPinHienTai=k.MaPinDangMuon WHERE n.MaTram=@s", ("@s", station)) == null, "Quyền sở hữu pin trong tủ cần được đối soát.");

        var batteries = db.Rows("SELECT p.MaPin FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram=@s ORDER BY n.SoThuTu", ("@s", station));
        for (int index = 0; index < batteries.Count; index++)
        {
            int soc = index < 2 ? Random.Shared.Next(90, 100) : Random.Shared.Next(10, 90);
            int soh = Random.Shared.Next(85, 101), temperature = Random.Shared.Next(25, 35), cycles = Random.Shared.Next(0, 351);
            decimal current = isolated ? 0 : soc >= 100 ? 0 : soc < 80 ? 15 : soc < 98 ? 8 : 2;
            decimal voltage = 67.2m + 12.8m * soc / 100m + current * .04m;
            db.Exec("UPDATE dbo.Pin SET SoC=@soc,SoH=@soh,NhietDo=@t,SoLanSac=@cycles,DienAp=@v,DongDien=@i,TrangThai=@state,NgayCapNhatCuoi=GETDATE() WHERE MaPin=@p", ("@soc", soc), ("@soh", soh), ("@t", temperature), ("@cycles", cycles), ("@v", voltage), ("@i", current), ("@state", soc >= 90 ? "DaDay" : "DangSac"), ("@p", batteries[index]["MaPin"]));
        }
        db.Exec("UPDATE dbo.NganTu SET CuaDangMo=0,KhoaBaoTri=@locked,TrangThaiNgan=CASE WHEN MaPinHienTai IS NULL THEN N'Trong' ELSE N'CoPin' END,NgayCapNhat=GETDATE() WHERE MaTram=@s; DELETE dbo.BssSlotCooldowns WHERE StationId=@s", ("@s", station), ("@locked", isolated));
        // Keep the alert audit rows while acknowledging the explicitly restarted scenario.
        db.Exec("UPDATE dbo.CanhBaoIoT SET DaXuLy=1,NguoiXuLy=N'Admin: reset giả lập',ThoiGianXuLy=GETDATE(),GhiChu=LEFT(CONCAT(N'Đặt lại mô phỏng; reset ',@id,N'. ',GhiChu),255) WHERE MaTram=@s AND DaXuLy=0", ("@id", key), ("@s", station));
        foreach (var row in db.Rows("SELECT Payload FROM dbo.BssCommands WHERE StationId=@s AND Status IN('Pending','Sent','Unconfirmed') AND JSON_VALUE(Payload,'$.command')='QUERY_STATUS'", ("@s", station)))
        {
            var command = JsonSerializer.Deserialize<DeviceCommand>((string)row["Payload"]!, SqlStore.Json)!;
            command.Status = "Cancelled";
            command.Note = "Đã đặt lại mô phỏng.";
            db.Exec("UPDATE dbo.BssCommands SET Status=@state,Payload=@p WHERE Id=@id", ("@state", command.Status), ("@p", JsonSerializer.Serialize(command, SqlStore.Json)), ("@id", command.CommandId));
        }
        var reset = new SimulationResetResult(key, station, Guid.NewGuid().ToString("N"));
        db.Exec("UPDATE dbo.BssDevices SET LeaseId=@lease,Sequence=-1,LastSeen=SYSDATETIMEOFFSET() WHERE StationId=@s; INSERT dbo.BssBusinessRequests(Id,Kind,Payload,Result) VALUES(@id,'SimulationReset',@p,@r)", ("@lease", reset.LeaseId), ("@s", station), ("@id", key), ("@p", payload), ("@r", JsonSerializer.Serialize(reset, SqlStore.Json)));
        return reset;
    });
}
