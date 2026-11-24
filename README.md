# Battery Swapping System (BSS)

Đồ án **Hệ thống quản lý mạng lưới tủ đổi pin tự động cho xe máy điện**, học phần Kỹ thuật Phần mềm Ứng dụng (KTPMUD).

Hệ thống gồm ứng dụng quản lý WPF trên Windows, giao diện Avalonia để thử nghiệm trên Ubuntu, backend ASP.NET Core, SQL Server và mô phỏng tủ pin Web3D qua MQTT.

## 1. Chức năng

| Thành phần / vai trò | Chức năng |
| --- | --- |
| Admin | Tổng quan hoạt động, doanh thu, quản lý trạm và pin, khách hàng, nạp ví demo và lịch sử giao dịch. |
| Kỹ thuật viên | Giám sát tủ/ngăn, kiểm tra thông số BMS, khóa bảo trì, xử lý quá nhiệt, ngắt và khôi phục sạc, hỗ trợ phiên đổi pin gặp sự cố. |
| Khách hàng | Xem thông tin tài xế, pin đang mượn, số dư và tiền giữ chỗ; tạo yêu cầu nạp ví chờ xác nhận; tạo phiên đổi pin và xem lịch sử cá nhân. |
| Web3D / IoT | Mô phỏng cắm/rút pin, cửa và khóa từng ngăn; gửi telemetry, nhận lệnh và phản hồi thiết bị qua MQTT. |
| BMS mô phỏng | Theo dõi SoC, SoH, điện áp, dòng và nhiệt độ; tốc độ ×1/×10/×60; tự ngắt sạc và khóa an toàn khi quá nhiệt. |
| Backend | Đăng nhập và phân quyền; giữ chỗ ví/ngăn/pin; thu phí đổi pin một lần; lưu phiên, lệnh, cảnh báo và phục hồi trạng thái dang dở. |

WPF và Avalonia dùng chung API; phạm vi giao diện và mức độ kiểm chứng của hai bản có thể khác nhau. QR/RFID mô phỏng chỉ đối chiếu hồ sơ đã đăng nhập, không cấp token hoặc tự tạo tài khoản. RFID/khóa/BMS phần cứng thật, thanh toán ngân hàng, SMS và máy in chưa được tích hợp. Nạp tiền và trừ phí sử dụng **ví nội bộ demo**; mô hình BMS chưa được hiệu chỉnh bằng pin thật.

## 2. Kiến trúc

```text
WPF (Windows) / Avalonia (Ubuntu)
                  │ HTTP REST /api/v1
                  ▼
          ASP.NET Core Server ───── SQL Server
                  │ MQTT
                  ▼
          Mosquitto cục bộ
                  │ MQTT qua WebSocket
                  ▼
          Web3D mô phỏng thiết bị
```

Backend phục vụ cả Web3D tại `http://127.0.0.1:5080/`. Desktop gọi backend qua HTTP; backend xử lý nghiệp vụ và dữ liệu SQL. Một trạm chỉ có một simulator chủ động.

## 3. Cấu trúc thư mục

```text
BatterySwappingSystem/
├── src/                             # Mã ứng dụng và thư viện
│   ├── BatterySwapping.App.Wpf/      # Giao diện Windows WPF
│   ├── BatterySwapping.App.Avalonia/ # Giao diện đa nền tảng
│   ├── BatterySwapping.ApiClient/    # Thư viện gọi HTTP API dùng chung
│   ├── BatterySwapping.Contracts/    # DTO và hợp đồng API/MQTT
│   ├── BatterySwapping.Core/         # Nghiệp vụ và truy cập SQL
│   ├── BatterySwapping.Server/       # REST API, xác thực, MQTT worker
│   ├── BatterySwapping.Web3D/        # Mô hình tủ 3D và giả lập thiết bị
│   └── BatterySwapping.Simulator/    # Launcher WPF tương thích
├── tests/                           # Kiểm thử, tách khỏi src
│   ├── BatterySwapping.Tests/        # Tích hợp API/MQTT/SQL và phục hồi
│   ├── BatterySwapping.ArchitectureTests/ # Mật khẩu và nghiệp vụ ví
│   ├── BatterySwapping.UiTests/      # XAML và điều hướng WPF
│   └── BatterySwapping.Web3D.Tests/  # Mô hình BMS JavaScript
├── Database/                        # SQL khởi tạo, seed, migration
├── docs/                            # Tài liệu API và hướng dẫn demo
├── scripts/                         # Chuẩn bị, chạy/dừng, kiểm thử
├── BatterySwappingSystem.sln         # Toàn bộ project
├── BatterySwappingSystem.Windows.slnf
├── BatterySwappingSystem.Ubuntu.slnf
├── BatterySwappingSystem.slnLaunch   # Profile chạy Server + WPF trong VS
├── Start-BSS.bat
├── Stop-BSS.bat
└── README.md
```

`.runtime/` chứa cấu hình và log cục bộ; `.tools/` chứa Mosquitto. Hai thư mục này cùng `bin/`, `obj/` không được đưa lên Git. `ApiClient` là thư viện, không phải ứng dụng chạy riêng.

## 4. Requirements

| Yêu cầu | Mục đích |
| --- | --- |
| .NET 8 SDK | Restore, build và chạy các project .NET. |
| Windows + .NET Desktop Runtime 8 | Chạy WPF; runtime thường đi kèm bộ cài SDK trên Windows. |
| Visual Studio 2022 hỗ trợ .NET 8 | Tùy chọn khi phát triển Windows; cài workload .NET Desktop và ASP.NET. |
| SQL Server đang chạy | Lưu dữ liệu nghiệp vụ; cấu hình Windows mặc định là `.\SQLEXPRESS`. |
| SSMS hoặc công cụ chạy SQL Server script | Khởi tạo database demo lần đầu. |
| Mosquitto có hỗ trợ WebSocket | Broker cho backend và Web3D. |
| PowerShell 7 (`pwsh`) | Chạy bộ script chuẩn bị và launcher Windows. |
| Trình duyệt hỗ trợ WebGL | Chạy Web3D. |
| Node.js | Chỉ cần khi chạy kiểm thử BMS JavaScript. |
| Ubuntu có môi trường desktop | Chạy thử giao diện Avalonia; không chạy WPF. |

Internet cần cho lần đầu tải SDK, Mosquitto và NuGet. Sau khi chuẩn bị đủ dependency, đường chạy demo Windows dùng các dịch vụ và asset cục bộ.

## 5. Chạy trên Windows

Các lệnh dưới đây chạy từ thư mục gốc repository, nơi có `BatterySwappingSystem.sln`.

### Bước 1 — Lấy mã nguồn

```powershell
git clone https://github.com/vthuan2108/BatterySwapping_KTPMUD.git
cd BatterySwapping_KTPMUD
```

### Bước 2 — Chuẩn bị SQL Server

**Nếu cài mới với database demo trống**, mở công cụ SQL và chạy lần lượt:

1. `Database/01_CreateDatabaseAndTables.sql`
2. `Database/02_TriggersAndProcedures.sql`
3. `Database/03_SampleSeedData.sql`
4. `Database/04_CreateViews.sql`
5. `Database/05_StoredProcedures.sql`

Script `01` có thao tác xóa bảng và script `03` nạp dữ liệu mẫu: **không chạy lại chúng trên database đang sử dụng**. Nếu đã có `BatterySwappingDB`, giữ nguyên dữ liệu. Backend tự áp dụng lần lượt các migration tăng dần `06_Runtime.sql`, `07_Business.sql`, `08_Maintenance.sql`, `09_Cooldowns.sql`, `10_AutoThermal.sql` khi khởi động.

### Bước 3 — Chuẩn bị broker và build

Đặt bộ Mosquitto Windows vào `.tools/mosquitto/`, gồm ít nhất `mosquitto.exe`, `mosquitto_passwd.exe` và các DLL đi kèm. Sau đó chạy:

```powershell
pwsh -File scripts/Prepare-Local.ps1
```

Nếu SQL Server dùng instance khác:

```powershell
pwsh -File scripts/Prepare-Local.ps1 -SqlServer 'TENMAY\INSTANCE' -Database 'BatterySwappingDB'
```

Script tạo `.runtime/server.json`, tài khoản broker, ACL và cấu hình Mosquitto; sau đó restore/build Release. Cấu hình SQL mặc định dùng Windows Authentication. Script không tạo các bảng nghiệp vụ ban đầu, nên cần hoàn thành bước 2 trước khi chạy server.

### Bước 4 — Khởi động

```powershell
.\Start-BSS.bat
```

Hoặc:

```powershell
pwsh -File scripts/Start-Local.ps1
```

Launcher mở broker, backend, WPF và Web3D. Để chỉ chạy dịch vụ:

```powershell
pwsh -File scripts/Start-Local.ps1 -NoBrowser -NoDesktop
```

| Địa chỉ / cổng | Dịch vụ |
| --- | --- |
| `http://127.0.0.1:5080/` | Web3D |
| `http://127.0.0.1:5080/api-docs` | Tài liệu API cục bộ |
| `http://127.0.0.1:5080/health` | Trạng thái server, SQL và MQTT |
| `127.0.0.1:18883` | MQTT TCP |
| `127.0.0.1:19001` | MQTT WebSocket |

### Chạy bằng Visual Studio

Sau khi chuẩn bị môi trường, mở `BatterySwappingSystem.sln` hoặc filter Windows. Chọn profile **BSS Local - Server + WPF**, hoặc cấu hình nhiều startup project gồm Server và App.Wpf, rồi F5. Mở địa chỉ Web3D ở bảng trên để chạy simulator. Không chạy đồng thời launcher và F5 cùng một backend trên cổng 5080.

### Dừng hệ thống

```powershell
.\Stop-BSS.bat
# Hoặc:
pwsh -File scripts/Stop-Local.ps1
```

Lệnh dừng quản lý các tiến trình do launcher mở. Nếu chạy bằng Visual Studio, dừng phiên debug trong Visual Studio.

## 6. Chạy thử Avalonia trên Ubuntu

Filter Ubuntu bỏ các project WPF khỏi quá trình build:

```bash
dotnet restore BatterySwappingSystem.Ubuntu.slnf
dotnet build BatterySwappingSystem.Ubuntu.slnf -c Release --no-restore
dotnet run --project src/BatterySwapping.App.Avalonia -c Release --no-build
```

Các lệnh này mở giao diện Avalonia. Để đăng nhập và dùng nghiệp vụ, cần backend tại `http://127.0.0.1:5080` trên cùng máy. Giao diện hiện chỉ chấp nhận backend loopback; chưa hỗ trợ trỏ trực tiếp sang máy Windows khác trong LAN.

Để chạy đầy đủ trên Ubuntu, cần tự chuẩn bị SQL Server có schema/dữ liệu tương ứng, broker Mosquitto với hai cổng và ACL như cấu hình Windows, cùng `.runtime/server.json` chứa connection string SQL và mật khẩu broker. Khi các dịch vụ đó đã sẵn sàng, chạy backend ở terminal riêng:

```bash
dotnet run --project src/BatterySwapping.Server -c Release --no-build
```

Sau đó mở Web3D và Avalonia. `Prepare-Local.ps1`, `Start-Local.ps1` và các file `.bat` hiện phục vụ đường chạy Windows; repository chưa có bộ cài/launcher tự động cho toàn hệ thống Ubuntu. Build filter Ubuntu đã được kiểm tra trên Windows; chưa kiểm chứng chạy thực tế trên Ubuntu.

## 7. Tài khoản demo

Có sẵn khi database đã nạp `Database/03_SampleSeedData.sql`:

| Vai trò | Tài khoản | Mật khẩu |
| --- | --- | --- |
| Admin | `admin` | `123456` |
| Kỹ thuật viên | `tech01` | `123456` |
| Khách hàng | `driver01`, `driver02` | `123456` |

Mỗi tài khoản mở chức năng theo vai trò. Đổi pin yêu cầu đăng nhập khách hàng; nhập QR hoặc số điện thoại không thay thế xác thực.

## 8. Demo nhanh

1. Khởi động hệ thống, mở Web3D và chờ kết nối Backend/MQTT.
2. Với khách mới, admin tạo hồ sơ (ví 0 đ) và bàn giao một pin kho có sẵn. Với tài khoản seed cũ, kiểm tra báo cáo bất nhất trước khi demo.
3. Khách tạo yêu cầu nạp; admin vào Cổng thanh toán giả lập xác nhận thành công/thất bại/hủy. Chỉ thành công mới tăng ví.
4. Đăng nhập khách trên Desktop hoặc Web3D, kiểm tra ví và bắt đầu một phiên đổi pin.
5. Trên simulator, cắm đúng pin cũ vào ngăn được chỉ định; chờ kiểm tra BMS và thu phí.
6. Rút pin mới khi được cho phép; kiểm tra phiên hoàn tất và lịch sử khách hàng.
7. Gây quá nhiệt trên Web3D: ba đợt đầu tự làm mát và phục hồi sau tối thiểu5 phút khi dưới35°C. Đợt thứ4 giữ khóa để Admin xác nhận an toàn.
8. Ngắt simulator để quan sát trạng thái offline và cách hệ thống giữ phiên cần hỗ trợ.

Chi tiết các tình huống mất kết nối, giữ chỗ và phục hồi: [docs/LOCAL-DEMO.md](docs/LOCAL-DEMO.md).

## 9. Kiểm thử

Sau khi build Release, chạy từ thư mục gốc:

```powershell
dotnet run --project tests/BatterySwapping.ArchitectureTests -c Release --no-build
# Nghiệp vụ có ghi, chỉ database kiểm thử riêng:
dotnet run --project tests/BatterySwapping.ArchitectureTests -c Release --no-build -- --business
node tests/BatterySwapping.Web3D.Tests/bms.test.js
```

Kiểm thử XAML/điều hướng WPF chỉ chạy trên Windows:

```powershell
dotnet run --project tests/BatterySwapping.UiTests -c Release --no-build
```

Kiểm thử tích hợp API/MQTT/SQL và phục hồi tiến trình:

```powershell
pwsh -File scripts/Test-Integration.ps1
```

Dừng phiên demo trước khi chạy tích hợp. Script sử dụng database riêng `BssArchitectureTests_20261121`; không chạy các bài kiểm thử có ghi lên database vận hành. Các project kiểm thử C# hiện là chương trình console, chạy bằng `dotnet run` như trên.

## 10. Lỗi thường gặp

| Hiện tượng | Kiểm tra |
| --- | --- |
| Đăng nhập báo `connection refused (127.0.0.1:5080)` | Backend chưa chạy hoặc đã thoát; kiểm tra `/health` và log server. |
| Server không kết nối SQL | Dịch vụ SQL, instance, database và quyền trong `.runtime/server.json`. |
| MQTT offline | Broker, tài khoản/ACL, hai cổng 18883 và 19001; xem log broker. |
| Không có pin để cấp | Trạng thái kết nối simulator, BMS, khóa bảo trì và tài nguyên đang được phiên khác giữ chỗ. |
| Launcher báo đã có phiên quản lý | Chạy `Stop-BSS.bat` trước khi khởi động lại. |

Log do launcher ghi tại `.runtime/server.out.log`, `.runtime/server.err.log`, `.runtime/broker.out.log` và `.runtime/broker.err.log`.

## 11. Tài liệu

- [API và MQTT](docs/API.md)
- [Chạy và demo cục bộ](docs/LOCAL-DEMO.md)
- [Ghi chú triển khai](docs/IMPLEMENTATION.md)

## Quy trình nghiệp vụ và nâng cấp

Xem [nghiệp vụ và migration](docs/BUSINESS-WORKFLOWS.md) trước khi chạy với database đã có dữ liệu. Các API nạp ví trực tiếp, đăng nhập QR/điện thoại và HTTP giả xác nhận cắm/rút đã bị bỏ.

### Khóa nhiệt tự động

Từ55°C, simulator tự làm mát và khóa ngăn ít nhất5 phút thực. Không cắm/rút/cấp pin hoặc mở ngăn trong thời gian này. Hết5 phút và dưới35°C: ba đợt đầu tự phục hồi sau phản hồi thiết bị; từ đợt thứ4 mới cần Admin xác nhận. tốc độx10/x60 không rút ngắn khóa. Bộ đếm được lưu qua tải lại/restart và hiển thị trên WPF, Avalonia, Web3D. Xem [quy trình chi tiết](docs/BUSINESS-WORKFLOWS.md).

### Reset Simulation và thông số ban đầu

Trong Web3D, mở **Công cụ demo thiết bị → Reset Simulation**. Nhập tài khoản Admin để đặt lại trạm đang chọn. SoC,SoH,nhiệt độ và số chu kỳ được tạo ngẫu nhiên có giới hạn; điện áp/dòng tính theo cùng mô hình sạc. Giữ nguyên mã/vị trí pin, ví và lịch sử. Hai pin đầu đang ở tủ được khởi tạo đủ SoC để demo đổi pin, các pin còn lại có SoC ngẫu nhiên thấp hơn.

Reset bị chặn khi trạm còn phiên đổi pin/bảo trì/lệnh chưa xác nhận. Yêu cầu reset bị mất phản hồi được thử lại cùng mã chống lặp, không random lại hai lần. Tải lại trang chỉ tiếp tục trạng thái đã lưu; không tự tạo một kịch bản mới. Bấm Reset mới bắt đầu kịch bản ngẫu nhiên mới.

Mức sạc tăng theo số giây thực đã trôi qua, dòng sạc và dung lượng thiết kế lấy từ pin; dòng giảm khi gần đầy. Tốc độ mô phỏng chỉ nhân thời gian vật lý, không nhân đồng hồ khóa an toàn5 phút.
