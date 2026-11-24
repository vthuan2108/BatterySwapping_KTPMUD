# BSS — chạy cục bộ

## Kiến trúc và phạm vi

WPF giữ bố cục XAML ban đầu. Desktop và công cụ WPF Simulator chỉ gọi HTTP API. ASP.NET Core quản lý nghiệp vụ, SQL và MQTT. Web3D giữ mô hình tủ ban đầu, đóng vai thiết bị mô phỏng: sinh BMS, thực hiện lệnh và phản hồi. Một trạm có một simulator chủ động; cửa sổ thứ hai chỉ xem.

Không tích hợp BMS/khóa/RFID thật, SMS, máy in hoặc thanh toán ngân hàng. QR hiện là định danh khách hàng, chỉ được dùng cùng tài khoản khách hàng đã đăng nhập. Ví là ví nội bộ demo.

## Chuẩn bị một lần

- Windows, SQL Server Express với `BatterySwappingDB` hiện có; không chạy lại script `01`/`03` trên dữ liệu đang dùng.
- .NET SDK hỗ trợ target net8.0 và runtime Windows Desktop 8. Server target .NET 8, cho phép roll-forward sang ASP.NET runtime mới đã cài (máy triển khai hiện có ASP.NET 10).
- Mosquitto Windows từ [trang chính thức](https://mosquitto.org/download/), cài vào `.tools/mosquitto` trong dự án. Bản chuẩn bị trên máy này là 2.1.4; thư mục `.tools` không đưa vào Git.
- Từ thư mục `BatterySwappingSystem`, chạy `pwsh -File scripts/Prepare-Local.ps1`. Lệnh restore/build cần dependency có sẵn hoặc Internet trong bước chuẩn bị. NuGet audit tắt trong chế độ chuẩn bị offline; đây không phải chứng nhận không có lỗ hổng dependency.

`Prepare-Local` sinh hai mật khẩu broker ngẫu nhiên và cấu hình riêng trong `.runtime` (không đưa vào Git). Không sửa dịch vụ MQTT sẵn có: broker dự án dùng TCP **18883** và WebSocket **19001**, chỉ lắng nghe `127.0.0.1`.

## Chạy và dừng

```powershell
pwsh -File scripts/Start-Local.ps1
pwsh -File scripts/Stop-Local.ps1
```

Backend/Web3D: `http://127.0.0.1:5080`. API: `/api-docs`. Health: `/health`. Các asset Web3D phục vụ tại backend, không tải CDN. Không cần launcher Python cũ.

Nếu chỉ cần backend: `Start-Local.ps1 -NoBrowser -NoDesktop`. Để mở thêm công cụ WPF, chạy executable Release của `BatterySwapping.Simulator`. Công cụ này dùng cùng API và màn hình WPF, không sinh dữ liệu SQL song song.

Tài khoản seed: `admin`, `tech01`, `driver01`, `driver02`, mật khẩu `123456`. Chỉ dùng khi database có dữ liệu demo tương ứng. Login thành công nâng mật khẩu legacy sang PBKDF2 có salt. Không log password/token; phiên đăng nhập bị thu hồi khi logout hoặc server khởi động lại.

## Kịch bản demo

1. Mở Web3D cho trạm BK01; chờ trạng thái Backend + MQTT. Đăng nhập `driver01` trên Web3D hoặc Desktop Kiosk; bắt đầu phiên ở một nơi, không tạo hai phiên độc lập.
2. Backend giữ chỗ phí, hai ngăn và pin. Chờ xác nhận mở ngăn; cắm đúng pin cũ bằng nút thao tác vật lý trên Web3D.
3. Backend kiểm tra BMS, trừ phí một lần và gửi lệnh cấp pin. Chỉ rút pin khi Web3D hiển thị nút cho phép. Hoàn tất khi server nhận sự kiện ngăn trống/cửa đóng.
4. Chọn một pin trên Web3D, gây quá nhiệt 65°C. Đối chiếu đúng trạm/ngăn/pin trên Desktop. Tạo phiếu bảo trì để xử lý cảnh báo; chỉ thông báo thành công sau ACK an toàn.
5. Ngắt toàn trạm từ Desktop; tất cả ngăn khóa và dòng về 0. Khôi phục chỉ sau khi toàn bộ trạm đủ mát. Có thể gọi `POST /api/v1/stations/{id}/commands` với `RESTORE_STATION` bằng tài khoản kỹ thuật/Admin.
6. Ngắt simulator: sau 5 giây trạm offline và chặn cấp pin. Sau khi đã nhận pin cũ, phiên chuyển cần hỗ trợ, giữ tài sản và dữ liệu phí. Phiên đã chốt phí có thể đối soát qua `POST /api/v1/swaps/{id}/resume` bằng tài khoản kỹ thuật/Admin, không thu phí lại.

Hủy trước khi nhận pin chờ CLOSE_SLOT ACK mới giải phóng ngăn. Phiên đã nhận pin nhưng chưa thu phí giữ trạng thái cần hỗ trợ để đối soát tại trạm; không tự hoàn tiền hoặc tự nhận là đã hủy. Lệnh chưa xác nhận sau 10 giây được truy vấn trạng thái, không tự nhả pin lại.

## Giao thức

REST `/api/v1`: JSON, bearer token, quyền kiểm tra tại backend. Các API được liệt kê ở `/api-docs`. Dữ liệu có ID ngăn database `MaNgan` và số ngăn vật lý `slotNumber`; MQTT luôn dùng số ngăn vật lý.

MQTT topic: `bss/station/{id}/telemetry`, `alerts`, `events`, `commands`, `acks`. Schema version 1; metadata gồm stationId, messageId, timestamp UTC, sequence và leaseId bí mật chỉ trao khi nhận lease. Snapshot công khai không chứa leaseId.

- Telemetry đầy đủ mọi ngăn, mỗi giây, QoS 0. Metadata thời gian/sequence và giới hạn số liệu được xác thực.
- Lệnh/sự kiện/ACK QoS 1, không retain. commandId chống thực thi lặp; sessionId liên kết giao dịch. Chỉ ACK cho lệnh Sent/Unconfirmed và trạng thái vật lý hợp lệ được chấp nhận.
- Lệnh: UNLOCK_SLOT, CLOSE_SLOT, LOCK_SLOT, EJECT_BATTERY, CUT_OFF, RESTORE_STATION, RESET_ALARM, REPLACE_BATTERY, TRIGGER_OVERHEAT, QUERY_STATUS.
- Web3D tự ngắt sạc/khóa từ 55°C dù mất server. Reset yêu cầu dưới 35°C và xác nhận đúng pin/ngăn; cấp pin yêu cầu SoC ≥90%, SoH ≥80%, nhiệt ≤45°C.

SoC tính theo Ah và dung lượng hiệu dụng; nhiệt dùng I²R với mô hình nhiệt tập trung; điện áp là đường cong giả định. Tốc độ ×1/×10/×60 chỉ tác động thời gian vật lý. Mô hình chưa hiệu chỉnh bằng pin thật.

## Kiểm thử và chẩn đoán

```powershell
dotnet build BatterySwappingSystem.sln --no-restore -c Release
dotnet run --project tests/BatterySwapping.ArchitectureTests --no-build -c Release
node tests/BatterySwapping.Web3D.Tests/bms.test.js
dotnet run --project tests/BatterySwapping.UiTests --no-build -c Release
```

Kiểm thử tích hợp: dùng `scripts/Test-Integration.ps1`. Chỉ script đó tạo/nạp dữ liệu database `BssArchitectureTests_20261121`; runner từ chối database ứng dụng. Script yêu cầu dừng phiên launcher trước khi chạy, rồi chạy backend/broker kiểm thử và tự dừng khi kết thúc. Sau đó chuẩn bị lại cấu hình mặc định nếu muốn demo trên database ứng dụng.

Log server/broker ở `.runtime/*.log`. Log lệnh có commandId/sessionId. Kiểm tra cổng nếu server không khởi động; không tắt các dịch vụ khác để giải phóng cổng. Thư mục `.runtime/processes.json` chỉ quản lý tiến trình do launcher mở.

## Giới hạn kiểm chứng hiện tại

API/MQTT/SQL và công thức BMS có kiểm thử tự động. Cần xác nhận trực quan WebGL, camera và toàn bộ tương tác WPF trên máy người dùng; công cụ trình duyệt của phiên triển khai không được cấp quyền Chrome. Chưa thực hiện thao tác tắt Internet của hệ điều hành; runtime được cấu hình chỉ dùng endpoint/asset cục bộ.

Đã kiểm chứng tự động: 22 ca API/MQTT, bao gồm khởi động lại tiến trình server trước nhận pin và sau trừ phí; 6 kiểm tra nạp XAML WPF cũ; kiểm tra PBKDF2 và mô hình BMS. Mô hình Web3D hiện biểu diễn tủ 8 ngăn.

## Quy trình mới

Xem [BUSINESS-WORKFLOWS.md](BUSINESS-WORKFLOWS.md): tạo hồ sơ ví0, bàn giao từ kho, nạp qua yêu cầu chờ và cổng giả lập, thay pin đăng ký sẵn. Kiểm tra bất nhất dữ liệu cũ trước demo; không tự sửa quyền giữ pin bằng seed.
