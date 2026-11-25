# BÁO CÁO TỔNG HỢP KIẾN TRÚC & QUY TRÌNH NGHIỆP VỤ HỆ THỐNG TRẠM ĐỔI PIN (BSS)

> **Dự án:** Hệ thống Trạm Đổi Pin Xe Điện Thông Minh (Battery Swapping System - BSS)  
> **Phiên bản:** 2.1 (Đồng bộ chuẩn nghiệp vụ Backend ASP.NET Core, Mô phỏng Web3D Vật lý, App Quản trị WPF Desktop)  
> **Định dạng:** Văn bản thuần túy (Text-only, không chèn hình ảnh hay mã sơ đồ đồ họa)

---

## 1. TỔNG QUAN HỆ THỐNG & KIẾN TRÚC CÁC PHÂN HỆ

Hệ thống **BSS** vận hành dựa trên sự kết hợp đồng bộ giữa 4 phân hệ chính:

### 1.1 Phân hệ Trạm Đổi Pin / Kiosk Mô Phỏng (Web3D & Kiosk Simulator)
- **Công nghệ:** HTML5, CSS3, JavaScript (Three.js, Web Worker, MQTT.js).
- **Chức năng:**
  - Mô phỏng tủ trạm 3D thời gian thực với 8 ngăn chứa pin cho 2 trạm (`TRAM-BK01` và `TRAM-CG02`).
  - Tích hợp giao diện màn hình cảm ứng Kiosk dành cho tài xế xe điện (Driver): hỗ trợ đăng nhập, xem số dư ví, khởi tạo/hủy đổi pin và cảnh báo rõ ràng nếu tài xế đang có phiên hoạt động tại trạm khác.
  - Mô phỏng động học BMS của từng viên pin: mức pin (SoC), độ chai pin (SoH), nhiệt độ tăng/giảm theo dòng sạc, điện áp, đóng/mở cửa tủ, rút/cắm pin và bật/tắt quạt làm mát chủ động trên ngăn bình thường (tách biệt với khóa làm mát tự động khi quá nhiệt).
  - Sử dụng luồng **Web Worker chạy ngầm** để phát gói tin Telemetry đều đặn mỗi 1 giây (1 Hz) lên MQTT Broker, không bị trình duyệt tạm dừng khi ẩn tab.

### 1.2 Phân hệ Máy Chủ Trung Tâm (BSS Web API & Core Engine)
- **Công nghệ:** C# (.NET 8), ASP.NET Core, MQTTnet, SQL Server.
- **Chức năng:**
  - Cung cấp cổng giao tiếp RESTful API xác thực JWT cho người dùng và thiết bị.
  - Tích hợp dịch vụ nền **MQTT Worker** lắng nghe và điều phối thông điệp IoT hai chiều giữa máy chủ và các trạm pin.
  - Lõi điều phối `BssEngine` quản trị toàn bộ máy trạng thái phiên đổi pin, cấp quyền trạm (Lease), kiểm soát an toàn nhiệt theo từng viên pin (`MaPin`), xử lý bảo trì, chính sách giá (`BssPolicy`) và giao dịch ví tiền.

### 1.3 Phân hệ Quản Trị & Giám Sát (WPF Desktop App)
- **Công nghệ:** C# (.NET 8), WPF (Windows Presentation Foundation), MVVM, Material Design.
- **Chức năng:**
  - Dành riêng cho **Quản trị viên (Admin)** và **Kỹ thuật viên (Technician)**.
  - Giám sát trạng thái Telemetry thời gian thực của các trạm (đèn báo Online/Offline có bộ lọc chống nhấp nháy).
  - Hiển thị trực quan 8 ngăn tủ sạc: mức pin, nhiệt độ, cảnh báo bảo vệ, tình trạng cửa, khóa bảo trì.
  - Phân tách rõ thao tác mở khóa thủ công (`UNLOCK_MAINTENANCE`) cho ngăn khóa bảo trì thông thường với quy trình xác nhận reset quá nhiệt khẩn cấp (`RESET_ALARM`) cho các pin bị quá nhiệt từ lần thứ 3 trở lên.
  - Tiếp nhận và xử lý cảnh báo IoT: đóng trực tiếp cảnh báo nhẹ/trung bình qua API mà không khóa ngăn, hoặc buộc tuân thủ cách ly nhiệt đối với cảnh báo quá nhiệt.
  - Theo dõi KPI mạng lưới: doanh thu ngày, tổng lượt đổi pin, số pin sẵn sàng, tồn kho pin (`Warehouse`), đối soát nạp tiền đa phương thức.

### 1.4 Phân hệ Lưu Trữ Dữ Liệu (SQL Server Database)
- **Cơ sở dữ liệu:** `BatterySwappingDB`.
- **Chức năng:** Lưu trữ bền vững tài khoản, ví khách hàng, danh mục pin, ngăn tủ, trạm pin, lịch sử đổi pin, lịch sử nạp tiền, phiên đổi pin, danh sách lệnh IoT, chính sách phí (`BssPolicy`), vị trí kho/cách ly (`BssBatteryLocations`), yêu cầu thanh toán (`BssPayments`), phiếu bảo trì (`BssMaintenance`, `BssMaintenanceLocks`), khóa cách ly nhiệt theo ngăn (`BssSlotCooldowns`) và hồ sơ sự cố nhiệt theo viên pin (`BssBatteryThermalState`, `BssBatteryThermalEvents`).

---

## 2. CÁC VAI TRÒ & PHÂN QUYỀN TRONG HỆ THỐNG

### Vai trò 1: Khách hàng / Tài xế xe điện (`KhachHang` / Driver)
- **Giao diện thao tác:** Màn hình Kiosk tại trạm (hoặc màn hình Kiosk trên Web3D).
- **Hành động được phép:**
  - Đăng nhập tài khoản bằng tên đăng nhập và mật khẩu; khi đăng xuất sẽ xóa sạch token và khóa yêu cầu phiên (`bss-request`) khỏi trình duyệt.
  - Xem thông tin số dư ví tiền hiện tại, số dư khả dụng và mã pin đang thuê mang theo xe.
  - Tự nạp tiền vào ví trên ứng dụng WPF App (`CustomerDashboardView` / `TopUpWalletDialog`) qua `POST /api/v1/wallet/topup` (endpoint này chỉ cho phép vai trò `KhachHang` có hồ sơ `MaKH` hợp lệ thực hiện, loại bỏ hoàn toàn việc mượn quyền Admin) hoặc tạo yêu cầu nạp tiền qua `POST /api/v1/payments`.
  - Bấm nút khởi tạo yêu cầu đổi pin tự động, thực hiện thao tác vật lý cắm pin cũ và rút pin mới.

### Vai trò 2: Kỹ thuật viên trạm (`KyThuatVien` / Technician)
- **Giao diện thao tác:** Ứng dụng Desktop WPF.
- **Hành động được phép:**
  - Giám sát chi tiết điện áp, dòng sạc, nhiệt độ từng ngăn tủ.
  - Khóa bảo trì ngăn tủ (`LOCK_SLOT`) hoặc mở khóa thủ công ngăn trống/ngăn bình thường không có sự cố nhiệt (`UNLOCK_MAINTENANCE`).
  - Mở/đóng cửa ngăn phục vụ kiểm tra (`UNLOCK_SLOT` / `CLOSE_SLOT`) khi ngăn không bị khóa an toàn.
  - Thực hiện phiếu bảo trì thay thế pin hỏng/chai dưới 80% SoH bằng pin đạt chuẩn từ kho `Warehouse` (`REPLACE_BATTERY`).
  - Xử lý và đóng các cảnh báo IoT mức nhẹ/trung bình không vướng khóa cách ly nhiệt (`POST /api/v1/alerts/{id}/resolve`).

### Vai trò 3: Quản trị viên hệ thống (`Admin`)
- **Giao diện thao tác:** Ứng dụng Desktop WPF.
- **Hành động được phép:**
  - Toàn quyền của Kỹ thuật viên.
  - Phê duyệt và xác nhận mở khóa an toàn cho pin/ngăn bị quá nhiệt từ lần thứ 3 trở lên (`RESET_ALARM` với cờ `AdminThermalReset`).
  - Quản lý danh sách trạm pin, thêm trạm, ngắt dừng hoạt động toàn trạm (`CUT_OFF`) và khôi phục trạm (`RESTORE_STATION`).
  - Quản lý giá dịch vụ đổi pin và loại pin hỗ trợ trong `BssPolicy` (`PUT /api/v1/policy`), mặc định phí đổi pin là **25.000 đ/lượt**.
  - Phê duyệt kết quả các yêu cầu nạp tiền chờ xử lý (`POST /api/v1/payments/{id}/result`), quản lý kho pin (`Warehouse` / `Quarantine`), thực hiện bàn giao pin thủ công từ kho cho khách hàng (`POST /api/v1/handovers`), xem báo cáo doanh thu và lịch sử giao dịch.

---

## 3. QUY TRÌNH NGHIỆP VỤ CHI TIẾT

---

### QUY TRÌNH 1: QUY TRÌNH ĐỔI PIN TỰ ĐỘNG TẠI TRẠM

Đây là quy trình nghiệp vụ cốt lõi nhất, diễn ra theo trình tự tuần tự 7 bước khép kín nhằm bảo đảm không thất thoát tài sản:

#### Bước 1: Khởi tạo yêu cầu tại Kiosk
- Tài xế tiếp cận trạm, đăng nhập tài khoản tại Kiosk và bấm **"Bắt đầu đổi pin"**.
- Kiosk gửi yêu cầu `POST /api/v1/swaps` kèm theo `StationId` và mã chống lặp `IdempotencyKey`.
- **Hệ thống Backend kiểm tra các điều kiện tiên quyết (Pre-conditions):**
  1. Trạm phải đang kết nối trực tuyến (Telemetry Online trong vòng 5 giây gần nhất).
  2. Trạm phải ở trạng thái hoạt động bình thường (`HoatDong`), không bị dừng bảo trì.
  3. Tài xế không có phiên đổi pin nào khác đang mở dở dang (`Active = 1`). Nếu tài xế chuyển sang xem trạm khác trên Web3D trong lúc còn phiên đang hoạt động, giao diện Kiosk sẽ hiển thị cảnh báo chỉ rõ trạm đang giữ phiên để tài xế quay về thao tác đúng trạm.
  4. Tài xế đang sở hữu 1 viên pin hợp lệ (đúng loại pin hỗ trợ trong `BssPolicy`, mặc định `Lithium LFP 72V-30Ah`).
  5. Số dư ví khả dụng (`SoDuVi - Hold`) phải lớn hơn hoặc bằng phí dịch vụ hiện hành trong `BssPolicy` (mặc định **25.000 đ**).
  6. Trạm phải có ít nhất **1 ngăn trống hợp lệ** (không khóa bảo trì, cửa đang đóng, không trong đợt cách ly nhiệt, không bị giữ bởi phiếu bảo trì hay phiên khác).
  7. Trạm phải có ít nhất **1 viên pin đạt chuẩn cấp** (`SoC >= 90%`, `SoH >= 80%`, `NhietDo <= 45°C`, không bị khóa bảo trì hay cách ly nhiệt).

#### Bước 2: Tạm giữ phí & Mở ngăn nhận pin cũ
- Hệ thống thực hiện lệnh giữ chỗ:
  - Tạm giữ khoản phí **25.000 đ** (theo `BssPolicy.Fee`) trong ví tài xế (chưa trừ tiền thực tế).
  - Đặt trước ngăn trống làm ngăn trả (`ReturnSlot`) và ngăn pin đầy làm ngăn nhận (`ReceiveSlot`).
  - Khởi tạo phiên đổi pin ở trạng thái `AwaitingOldBattery`.
- Máy chủ gửi lệnh qua MQTT: `UNLOCK_SLOT` tới ngăn trả pin.
- Cửa ngăn trả pin tại trạm tự động bật mở. Màn hình Kiosk hướng dẫn: *"Vui lòng đưa pin cũ vào ngăn số X và đóng cửa"*.

#### Bước 3: Đưa pin cũ vào ngăn trả
- Tài xế rút pin cũ từ xe và cắm vào ngăn trả pin, sau đó đóng cửa ngăn tủ lại.
- Cảm biến tại ngăn tủ ghi nhận: có pin hiện diện, cửa đã đóng, nhận diện mã pin qua giao tiếp nội bộ BMS.
- Trạm gửi thông điệp sự kiện MQTT: `OLD_INSERTED` kèm toàn bộ dữ liệu đo lường của viên pin vừa cắm.

#### Bước 4: Kiểm tra pin cũ & Trừ tiền dịch vụ
- Backend tiếp nhận sự kiện `OLD_INSERTED` và đối soát:
  - Mã viên pin vừa cắm phải trùng khớp 100% với mã pin mà tài xế đang thuê.
  - Cửa ngăn trả pin phải ở trạng thái đóng hoàn toàn.
  - Nhiệt độ pin cũ phải nằm trong ngưỡng kiểm soát (`< 55°C`) và ngăn nhận pin mới vẫn sẵn sàng.
- **Xử lý tài chính và kho:**
  - Thu hồi quyền giữ pin cũ khỏi tài khoản tài xế (`MaPinDangMuon = NULL`).
  - Gán viên pin cũ vào ngăn tủ trạm và bắt đầu kích hoạt sạc lại.
  - **CHÍNH THỨC TRỪ TIỀN VÍ:** Trừ đúng khoản phí `SwapSession.Fee` (mặc định **25.000 đ**) từ số dư ví khách hàng.
  - Tạo bản ghi giao dịch trong bảng `LichSuDoiPin` ở trạng thái `DangXuLy`.
- Chuyển trạng thái phiên đổi pin sang `Dispensing`.

#### Bước 5: Mở ngăn cấp pin mới
- Hệ thống gửi lệnh MQTT: `EJECT_BATTERY` tới ngăn chứa pin mới (`ReceiveSlot`).
- Chốt điện ngăn mở ra, cơ cấu đẩy pin nhô viên pin đầy ra ngoài.
- Đèn LED viền ngăn tủ chuyển sang màu xanh lá báo hiệu vị trí pin cần lấy. Màn hình Kiosk hiển thị: *"Mời bạn rút pin mới tại ngăn số Y và đóng cửa lại"*.

#### Bước 6: Khách hàng rút pin mới
- Tài xế rút viên pin mới ra khỏi ngăn tủ và đẩy cửa đóng lại.
- Cảm biến tại ngăn tủ ghi nhận: ngăn đã trống, cửa đã đóng an toàn.
- Trạm phát sự kiện MQTT: `NEW_TAKEN` lên máy chủ.

#### Bước 7: Cập nhật quyền sở hữu & Hoàn tất phiên
- Backend tiếp nhận sự kiện `NEW_TAKEN`:
  - Xác nhận ngăn cấp đã rỗng và cửa đã đóng kín.
  - Gán mã pin mới vào tài khoản tài xế (`MaPinDangMuon = NewBatteryId`).
  - Đổi trạng thái ngăn cấp trên hệ thống thành `Trong` (Trống).
  - Cập nhật bản ghi `LichSuDoiPin` thành `HoanThanh` và lưu thời gian kết thúc.
  - Chuyển trạng thái phiên đổi pin thành `Completed`.
- Màn hình Kiosk thông báo: *"Đổi pin thành công! Chúc bạn thượng lộ bình an!"*.

---

### QUY TRÌNH 2: NẠP TIỀN VÍ, ĐỐI SOÁT TÀI CHÍNH & QUẢN LÝ KHO PIN

1. **Nạp tiền tự động trên ứng dụng WPF App (Chỉ dành cho `KhachHang`):**
   - Tài xế thực hiện nạp tiền qua giao diện `CustomerDashboardView` / `TopUpWalletDialog` trên ứng dụng WPF App (`POST /api/v1/wallet/topup`; giao diện Web3D Kiosk chỉ hiển thị số dư ví và thao tác đổi pin).
   - Endpoint này áp dụng kiểm soát phân quyền nghiêm ngặt chỉ cho vai trò `KhachHang` có hồ sơ `CustomerId` hợp lệ tự nạp vào ví của chính mình (không cho phép Admin hay vai trò khác dùng endpoint tự nạp).
   - Hệ thống kiểm tra số tiền nguyên hợp lệ (`1` đến `10.000.000 đ`), tạo yêu cầu nạp và tự động chốt `Succeeded`, cộng trực tiếp vào `KhachHang.SoDuVi` và lưu lịch sử vào `LichSuNapTien`.

2. **Nạp tiền qua yêu cầu thanh toán đa phương thức (`BssPayments`):**
   - Khách hàng hoặc Admin khởi tạo yêu cầu nạp qua `POST /api/v1/payments` kèm theo `Method` hợp lệ gồm: `CongGiaLap`, `ChuyenKhoan`, `TienMat`, hoặc `TheNganHang`.
   - Giao dịch mới có trạng thái `Pending` kèm `Method` đã chọn.
   - Khi Admin xác nhận kết quả (`POST /api/v1/payments/{id}/result` với `Succeeded`), hệ thống cộng tiền vào `SoDuVi` và ghi nhận đúng `PhuongThuc` vào bảng `dbo.LichSuNapTien` để phục vụ đối soát.

3. **Cơ chế an toàn tạm giữ (Hold) vs Trừ tiền (Deduct):**
   - Khi bấm bắt đầu đổi pin: Số tiền **25.000 đ** (theo `BssPolicy`) chỉ bị đánh dấu tạm giữ (`Hold`), số dư khả dụng giảm nhưng tiền trong ví chưa bị trừ.
   - Nếu phiên bị hủy trước khi đưa pin cũ vào hoặc hết hạn chờ: Lệnh giữ tiền tự động được giải phóng, khách hàng không mất tiền.
   - Chỉ khi pin cũ đã vào ngăn an toàn, cửa đã đóng và hệ thống chuyển sang cấp pin mới: Khoản phí mới bị trừ dứt điểm.

4. **Quản lý tồn kho pin (`Warehouse` / `Quarantine`) & Bàn giao (`Handover`):**
   - Endpoint `GET /api/v1/inventory` phân loại rõ vị trí từng viên pin: `Cabinet` (đang nằm trong tủ trạm), `Customer` (đang gắn trên xe khách), `Warehouse` (đang sẵn sàng trong kho), `Quarantine` (đang cách ly/thu hồi do lỗi hoặc `SoH < 80%`), hoặc `Conflict` (xung đột sở hữu).
   - Pin trong kho (`Warehouse`) muốn dùng để thay thế bảo trì (`REPLACE_BATTERY`) hoặc bàn giao trực tiếp cho khách (`POST /api/v1/handovers`) phải không bị đặt giữ (`Reserved = false`), khớp loại `Lithium LFP 72V-30Ah`, `SoC >= 90%`, `SoH >= 80%`, `NhietDo <= 45°C` (`< 35°C` khi lắp vào tủ).
   - Dữ liệu mẫu khởi tạo chuẩn bị sẵn các viên pin đạt chuẩn trong kho (như `PIN-015` và `PIN-016`) sẵn sàng cho quy trình thay thế và bàn giao.

---

### QUY TRÌNH 3: BẢO VỆ AN TOÀN NHIỆT, MỞ KHÓA THỦ CÔNG & XỬ LÝ CẢNH BÁO

Nhiệt độ các khối pin lithium và trạng thái khóa ngăn tủ được giám sát chặt chẽ nhằm bảo đảm an toàn cháy nổ nhưng vẫn thuận tiện cho vận hành bảo trì:

#### 1. Các ngưỡng nhiệt độ quy chuẩn:
- **Dưới 45.0°C (Mức bình thường):** Pin được sạc dòng điện tiêu chuẩn (tối đa 15A theo đường cong sạc BMS), đủ điều kiện cấp cho khách hàng nếu đạt `SoC >= 90%` và `SoH >= 80%`.
- **Từ 45.0°C đến dưới 55.0°C (Mức cảnh báo):** Hệ thống BMS cảnh báo nhiệt độ cao; ngăn này tạm thời không được đưa vào danh sách cấp pin cho khách hàng. Người vận hành trên Web3D có thể bật/tắt quạt làm mát chủ động cho ngăn bình thường mà không làm khóa cứng ngăn.
- **Từ 55.0°C trở lên (Mức sự cố quá nhiệt):** Kích hoạt lập tức quy trình cách ly khẩn cấp.

#### 2. Quy trình kích hoạt cách ly khẩn cấp & Theo dõi sự cố theo `MaPin`:
- Ngay khi cảm biến phát hiện nhiệt độ `>= 55.0°C` (hoặc cờ `IsOverheated = true`):
  - Lập tức ngắt dòng sạc về 0A (`DongDien = 0`), bật quạt làm mát tự động (`isCooling = true`).
  - Khóa chốt cơ điện của ngăn tủ (`KhoaBaoTri = 1`), đóng cửa và ngăn không cho mở cửa.
  - Ghi nhận đợt cách ly vào bảng `BssSlotCooldowns` với mốc thời gian: `UnlockAfter = Thời điểm hiện tại + 5 phút`.
  - **Theo dõi lũy kế theo `MaPin` (`BatteryId`):** Hệ thống cập nhật bộ đếm sự cố `IncidentCount` và `TotalIncidents` trong bảng `BssBatteryThermalState` gắn với chính mã viên pin `BatteryId` (đồng thời ghi nhật ký vào `BssBatteryThermalEvents`). Nhờ theo dõi theo `MaPin` thay vì chỉ theo số ngăn, việc tháo pin lắp sang ngăn khác hoặc trạm khác không thể xóa hay lách lịch sử quá nhiệt của viên pin đó.

#### 3. Cơ chế phục hồi nhiệt 3 cấp độ:
- **Cấp độ 1 & 2 (Pin quá nhiệt lần 1 hoặc lần 2):**
  - Quạt làm mát tự động chạy liên tục (không cho phép tắt thủ công khi đang khóa nhiệt).
  - Bắt buộc chờ đủ **5 phút thời gian thực** (đồng hồ thực, không bị rút ngắn bởi hệ số tăng tốc mô phỏng) và nhiệt độ đo được hạ xuống **dưới 35.0°C**.
  - Khi thỏa mãn đồng thời 2 điều kiện trên, máy chủ tự động phát lệnh `AUTO_RESET_ALARM` để mở khóa ngăn tủ (giữ nguyên khóa trạm nếu trạm đang ở trạng thái `TamDung`), ghi nhận sự kiện `AutoReset` vào `BssBatteryThermalEvents` và đưa ngăn về trạng thái sẵn sàng sạc.
- **Cấp độ 3 (Pin quá nhiệt từ lần thứ 3 trở lên):**
  - Hệ thống đánh dấu cờ `RequiresAdmin = 1` trên cả `BssBatteryThermalState` và `BssSlotCooldowns`.
  - Dù đã hết 5 phút và nhiệt độ đã hạ dưới 35.0°C, ngăn tủ vẫn tiếp tục bị khóa cứng tuyệt đối; lệnh `AUTO_RESET_ALARM` sẽ bị từ chối.
  - **Bắt buộc Quản trị viên (Admin):** Kiểm tra trực tiếp trên ứng dụng Desktop WPF và tạo phiếu bảo trì xác nhận reset quá nhiệt (`RESET_ALARM` với `AdminThermalReset = true`). Khi thiết bị ACK thành công ở nhiệt độ `< 35°C`, hệ thống xóa khóa cách ly, đưa chuỗi `IncidentCount` về 0, tắt `RequiresAdmin = 0`, lưu `LastAdminResetAt` và ghi sự kiện `AdminReset`.

#### 4. Tách biệt Mở khóa thủ công (`UNLOCK_MAINTENANCE`) với Reset quá nhiệt (`RESET_ALARM`):
- **Ngăn bị khóa bảo trì thông thường (`LOCK_SLOT` trên ngăn trống hoặc ngăn bình thường không có cách ly nhiệt):**
  - Kỹ thuật viên/Admin bấm mở khóa trên Desktop WPF sẽ gọi lệnh **`UNLOCK_MAINTENANCE`** (`POST /api/v1/stations/{station}/commands`).
  - Lệnh được chấp nhận khi trạm đang `HoatDong`, ngăn không có `BssSlotCooldowns` đang mở, không thuộc `BssMaintenanceLocks`/`BssReservations` và nhiệt độ `< 55.0°C` (áp dụng được cho cả ngăn trống lẫn ngăn có pin ở nhiệt độ an toàn, không đòi hỏi hạ dưới `35°C`).
  - Trạm Web3D thực thi mở khóa (`isLocked = false`) và gửi `ACK` xác nhận để cập nhật `KhoaBaoTri = 0`.
- **Ngăn đang trong đợt cách ly nhiệt (`BssSlotCooldowns` chưa đóng):**
  - Tuyệt đối bị từ chối lệnh `UNLOCK_MAINTENANCE`, bắt buộc đi qua quy trình chờ đủ 5 phút, `< 35°C` và xác nhận phục hồi nhiệt (`AUTO_RESET_ALARM` hoặc `RESET_ALARM`).

#### 5. Xử lý cảnh báo IoT mức nhẹ/trung bình (`POST /api/v1/alerts/{id}/resolve`):
- Đối với các cảnh báo mức nhẹ/trung bình (như cảnh báo nhiệt độ 45°C - 54.9°C hoặc cảnh báo thông tin không đi kèm khóa cách ly nhiệt):
  - Kỹ thuật viên/Admin xác nhận đóng cảnh báo trực tiếp qua endpoint `POST /api/v1/alerts/{id}/resolve` (kèm ghi chú tùy chọn).
  - Hệ thống đánh dấu `CanhBaoIoT.DaXuLy = 1`, lưu `NguoiXuLy` và `GhiChu` mà không cần gửi lệnh phần cứng `RESET_ALARM` gây khóa ngăn tủ ngoài ý muốn.
- Nếu cảnh báo gắn với ngăn đang có khóa cách ly nhiệt (`BssSlotCooldowns` đang mở), nhiệt độ `>= 55°C` hoặc pin đang ở trạng thái `LoiQuaNhiet`, endpoint sẽ từ chối (HTTP `409`) để bảo đảm cảnh báo quá nhiệt chỉ được đóng sau khi hoàn tất quy trình làm mát an toàn.

---

### QUY TRÌNH 4: ĐỒNG BỘ IOT HAI CHIỀU & CHỐNG NGHẼN TRÌNH DUYỆT

Để đảm bảo kết nối giữa trạm giả lập Web3D và ứng dụng Quản trị WPF luôn ổn định:

1. **Chu kỳ gửi Telemetry 1 Giây:**
   - Mỗi giây, trạm mô phỏng gửi 1 gói tin JSON qua giao thức MQTT tới chủ đề: `bss/station/{Mã_Trạm}/telemetry` chứa đầy đủ thông số 8 ngăn (SoC, SoH, nhiệt độ, điện áp, dòng điện, trạng thái cửa, trạng thái khóa, cờ quá nhiệt).
2. **Cơ chế Web Worker chống trình duyệt bóp nghẽn:**
   - Tab Web3D sử dụng **Web Worker riêng biệt** để phát nhịp mỗi `1.000 ms`, giúp dữ liệu Telemetry không bị trình duyệt làm chậm khi người dùng chuyển sang làm việc trên ứng dụng WPF.
3. **Đồng bộ Snapshot thông minh trên Web3D:**
   - Khi một ngăn đang ở trạng thái rảnh (không có sự kiện chờ gửi, không có lệnh chờ ACK, không thuộc phiên đổi pin đang mở, không bị khóa nhiệt và cửa đang đóng), `snapshotRefresh` tự động đồng bộ các thay đổi hợp lệ từ máy chủ như cập nhật `KhoaBaoTri` hoặc thay thế pin mà không ghi đè dữ liệu mô phỏng vật lý đang chạy.
4. **Cơ chế Timeout 5 Giây & Debouncing trên App WPF:**
   - Máy chủ theo dõi `LastSeen` trong bảng `BssDevices`; nếu quá 5 giây không có gói tin mới, trạm được đánh dấu `Offline`.
   - Ứng dụng WPF thăm dò định kỳ và áp dụng bộ lọc chống rung lắc (debouncing) trước khi hiển thị cảnh báo mất kết nối.
5. **Quản lý cache lệnh bảo trì trên App WPF:**
   - Khi một lệnh bảo trì trả về trạng thái `Failed` hoặc `Cancelled`, ứng dụng WPF tự động giải phóng khóa chống lặp (`MaintenanceKeys`) để kỹ thuật viên/Admin có thể thao tác lại bằng `RequestId` mới thay vì bị kẹt lệnh cũ.

---

## 4. BẢNG MÔ HÌNH DỮ LIỆU CỐT LÕI (DATABASE SCHEMAS)

Hệ thống quản lý dữ liệu thông qua các bảng quan hệ chính sau:

### Bảng 1: `KhachHang` (Thông tin tài xế & Ví tiền)
- `MaKH` (Khóa chính): Mã định danh duy nhất của khách hàng.
- `MaTK` (Khóa ngoại): Liên kết tới bảng tài khoản người dùng (`TaiKhoan`).
- `HoTen`, `SoDienThoai`, `Email`, `BienSoXe`: Hồ sơ khách hàng.
- `SoDuVi`: Số dư ví tiền (VND). Số dư khả dụng thực tế bằng `SoDuVi - SUM(Hold)` của các phiên đang hoạt động.
- `MaPinDangMuon` (Khóa ngoại): Mã viên pin mà khách hàng hiện đang giữ trên xe (`NULL` nếu chưa giữ pin).

### Bảng 2: `Pin` (Danh mục pin lưu hành)
- `MaPin` (Khóa chính): Mã định danh viên pin (ví dụ: `PIN-001` đến `PIN-016`).
- `LoaiPin`: Chuẩn kết nối pin (mặc định `Lithium LFP 72V-30Ah`).
- `DungLuongThietKe`: Dung lượng thiết kế (Ah, mặc định `30`).
- `SoC`: Mức sạc hiện tại (`0% - 100%`).
- `SoH`: Sức khỏe pin (`0% - 100%`). Pin `< 80%` không đạt chuẩn cấp phát và cần thu hồi bảo trì.
- `NhietDo`, `DienAp`, `DongDien`: Thông số điện hóa BMS theo thời gian thực.
- `TrangThai`: Tình trạng pin (`DaDay`, `DangSac`, `DangCap`, `DangSuDung`, `KhaDung`, `DaThuHoi`, `LoiQuaNhiet`).

### Bảng 3: `NganTu` (Các ngăn sạc tại trạm pin)
- `MaTram` & `SoThuTu` (Khóa chính/định danh ngăn): Xác định ngăn số `1..8` tại từng trạm.
- `MaPinHienTai` (Khóa ngoại): Mã viên pin đang nằm trong ngăn (`NULL` nếu ngăn trống).
- `CuaDangMo`: Trạng thái cửa ngăn tủ (`0`: Đóng, `1`: Mở).
- `KhoaBaoTri`: Trạng thái khóa bảo trì/an toàn (`0`: Bình thường, `1`: Đang khóa).
- `TrangThaiNgan`: Tình trạng ngăn tủ (`Trong`, `CoPin`, `DangSac`, `Loi`).

### Bảng 4: `BssPolicy` (Chính sách giá & chuẩn pin)
- `Id` (Khóa chính, cố định `= 1`).
- `Fee`: Phí đổi pin mặc định (**`25000`** đồng/lượt).
- `SupportedBatteryType`: Chuẩn pin hợp lệ toàn hệ thống (`Lithium LFP 72V-30Ah`).

### Bảng 5: `BssSlotCooldowns`, `BssBatteryThermalState` & `BssBatteryThermalEvents` (An toàn nhiệt)
- `BssSlotCooldowns`: Lưu trạng thái cách ly nhiệt hiện hành tại ngăn (`StationId`, `SlotNumber`, `BatteryId`, `StartedAt`, `UnlockAfter`, `IncidentCount`, `RequiresAdmin`, `ClearedAt`).
- `BssBatteryThermalState`: Lưu trạng thái lũy kế theo từng viên pin (`BatteryId` là khóa chính, `IncidentCount`, `TotalIncidents`, `RequiresAdmin`, `LastStationId`, `LastSlotNumber`, `LastStartedAt`, `LastClearedAt`, `LastAdminResetAt`).
- `BssBatteryThermalEvents`: Nhật ký kiểm toán từng lần quá nhiệt (`Overheat`) và phục hồi (`AutoReset`, `ManualReset`, `AdminReset`) gắn với `BatteryId` và `ActorCommandId`.

### Bảng 6: `BssPayments` & `LichSuNapTien` (Thanh toán nạp ví)
- `BssPayments`: Yêu cầu nạp ví chống lặp (`Id`, `CustomerId`, `Amount`, `Status`, `Method`, `CreatedAt`, `ResolvedAt`, `ResolvedBy`).
- `LichSuNapTien`: Lịch sử nạp thành công lưu `MaKH`, `SoTien`, `PhuongThuc` (`CongGiaLap`, `ChuyenKhoan`, `TienMat`, `TheNganHang`), `MaGiaoDichNganHang`, `TrangThai`.

### Bảng 7: `BssBatteryLocations`, `BssHandovers` & `LichSuDoiPin` (Kho & Đối soát đổi pin)
- `BssBatteryLocations`: Đánh dấu vị trí pin ngoài tủ/khách hàng (`Warehouse` hoặc `Quarantine`).
- `BssHandovers`: Lịch sử bàn giao pin trực tiếp từ kho `Warehouse` cho khách hàng.
- `LichSuDoiPin`: Đối soát giao dịch đổi pin (`MaGD`, `MaKH`, `MaTram`, `MaPinTra`, `SoC_PinTra`, `NganTra`, `MaPinNhan`, `SoC_PinNhan`, `NganNhan`, `PhiDoiPin`, `TrangThai`, `ThoiGianBatDau`, `ThoiGianKetThuc`).

---

## 5. DANH MỤC LỆNH ĐIỀU KHIỂN THIẾT BỊ (IOT COMMAND CODES)

Khi máy chủ cần điều khiển phần cứng của trạm, các lệnh sau sẽ được đẩy xuống hàng đợi MQTT (`bss/station/{MaTram}/commands`):

1. **`UNLOCK_SLOT`:** Mở chốt khóa cơ điện để cửa ngăn bật mở (dùng khi mở ngăn trả pin hoặc kiểm tra khi ngăn không bị khóa bảo trì).
2. **`CLOSE_SLOT`:** Yêu cầu trạm xác nhận đóng cửa ngăn tủ.
3. **`LOCK_SLOT`:** Khóa bảo trì thủ công một ngăn tủ (`KhoaBaoTri = 1`, đóng cửa và ngắt dòng sạc).
4. **`UNLOCK_MAINTENANCE`:** Mở khóa bảo trì thủ công (`KhoaBaoTri = 0`) cho ngăn trống hoặc ngăn bình thường có nhiệt độ `< 55°C` và không vướng khóa cách ly nhiệt.
5. **`EJECT_BATTERY`:** Đẩy pin mới đạt chuẩn ra ngoài cho khách hàng rút (kèm mở khóa và đèn LED định vị).
6. **`RETURN_OLD_BATTERY`:** Mở ngăn trả lại pin cũ cho khách hàng trong tình huống phiên đổi pin bị hủy trước khi chốt cấp pin mới.
7. **`CUT_OFF`:** Lệnh khẩn cấp: lập tức ngắt toàn bộ dòng sạc tất cả các ngăn về 0A, khóa toàn bộ ngăn và chuyển trạm sang `TamDung`.
8. **`RESTORE_STATION`:** Khôi phục trạm hoạt động (`HoatDong`) trở lại sau khi toàn bộ ngăn `< 35°C`, không còn khóa nhiệt, không còn cảnh báo chưa xử lý và không còn phiếu bảo trì mở.
9. **`AUTO_RESET_ALARM`:** Lệnh máy chủ tự động xóa cảnh báo nhiệt độ và mở khóa ngăn tủ (chỉ áp dụng cho quá nhiệt lần 1 và lần 2 khi đã đủ 5 phút thời gian thực và nhiệt độ `< 35°C`).
10. **`RESET_ALARM`:** Lệnh từ phiếu bảo trì của Quản trị viên (Admin) trên App WPF để xác nhận mở khóa cho pin/ngăn quá nhiệt lần thứ 3 trở lên sau khi đủ 5 phút và nhiệt độ `< 35°C`.
11. **`REPLACE_BATTERY`:** Lệnh từ phiếu bảo trì của Kỹ thuật viên/Admin để tháo pin cũ (đưa vào `Quarantine`) và lắp pin đạt chuẩn từ kho `Warehouse` vào ngăn tủ.
12. **`TRIGGER_OVERHEAT`:** Lệnh mô phỏng sự cố quá nhiệt (`65°C`) phục vụ kiểm thử an toàn trên Web3D.
13. **`QUERY_STATUS`:** Lệnh truy vấn lại trạng thái của một lệnh trước đó đang ở trạng thái chưa xác nhận (`Unconfirmed`).

---

## 6. KẾT LUẬN & ĐIỂM NỔI BẬT CỦA HỆ THỐNG

1. **Quy trình chặt chẽ, khép kín:** Từng bước trong phiên đổi pin đều có điều kiện ràng buộc và hậu điều kiện bảo đảm nguyên tắc "nhận pin cũ hợp lệ rồi mới trừ đúng phí **25.000 đ** theo `BssPolicy` và nhả pin mới".
2. **An toàn cháy nổ chủ động theo mã pin:** Theo dõi lũy kế sự cố quá nhiệt theo `MaPin` qua `BssBatteryThermalState` kết hợp khóa cách ly 5 phút thời gian thực và điều kiện `< 35°C` giúp ngăn chặn triệt để nguy cơ lách giới hạn an toàn.
3. **Vận hành bảo trì linh hoạt & tách bạch:** Phân tách rõ ràng giữa khóa/mở khóa bảo trì thủ công (`LOCK_SLOT` / `UNLOCK_MAINTENANCE`), đóng cảnh báo nhẹ qua API (`POST /api/v1/alerts/{id}/resolve`), và xác nhận Admin cho quá nhiệt cấp độ 3 (`RESET_ALARM`), đi kèm quản lý tồn kho `Warehouse` và đối soát thanh toán đa phương thức.
