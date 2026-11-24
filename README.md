# Battery Swapping System

Hệ thống tủ đổi pin: backend ASP.NET Core, SQL Server, MQTT và mô phỏng Web3D.

## Cấu trúc

- `src/BatterySwapping.App.Wpf`: ứng dụng Windows WPF.
- `src/BatterySwapping.App.Avalonia`: giao diện đa nền tảng để thử trên Ubuntu.
- `src/BatterySwapping.ApiClient`: thư viện HTTP dùng chung (namespace `BatterySwapping.Client` được giữ để tương thích).
- `src/BatterySwapping.Server`: REST API, xác thực và MQTT.
- `src/BatterySwapping.Core`: nghiệp vụ và truy cập SQL.
- `src/BatterySwapping.Contracts`: DTO và hợp đồng giao tiếp.
- `src/BatterySwapping.Web3D`: mô phỏng thiết bị, được server phục vụ.
- `src/BatterySwapping.Simulator`: launcher WPF tương thích, dùng chung màn hình App.
- `tests/`: kiểm thử C#, độc lập với mã ứng dụng trong `src/`.
- `tests/BatterySwapping.Web3D.Tests/`: kiểm thử JavaScript của mô hình BMS.
- `Database/`: SQL khởi tạo, dữ liệu demo và migration tăng dần.
- `scripts/`: chuẩn bị, chạy/dừng và kiểm thử tích hợp.
- `docs/`: tài liệu API và demo.

## Mở và build

- Toàn bộ project: `BatterySwappingSystem.sln`.
- Windows: `BatterySwappingSystem.Windows.slnf`.
- Ubuntu: `BatterySwappingSystem.Ubuntu.slnf` (không chứa WPF).

```sh
dotnet build BatterySwappingSystem.Windows.slnf -c Release
dotnet build BatterySwappingSystem.Ubuntu.slnf -c Release
dotnet run --project src/BatterySwapping.App.Avalonia
```

Filter Ubuntu dùng để build và thử giao diện đa nền tảng; backend vẫn cần SQL Server và MQTT theo cấu hình môi trường.

Xem [hướng dẫn demo cục bộ](docs/LOCAL-DEMO.md) và [API/MQTT](docs/API.md).
Trên Windows, chuẩn bị theo hướng dẫn rồi chạy `Start-BSS.bat`. Cấu hình riêng ở `.runtime/`, công cụ cục bộ ở `.tools/`; cả hai không đưa vào Git.

Không chạy lại script xóa/tạo bảng trên database đang sử dụng; `Database/06_Runtime.sql` là migration tăng dần.
