# BÁO CÁO TỔNG HỢP KIẾN TRÚC & QUY TRÌNH NGHIỆP VỤ HỆ THỐNG TRẠM ĐỔI PIN (BSS)

> **Dự án:** Hệ thống Trạm Đổi Pin Xe Điện Thông Minh (Battery Swapping System - BSS)  
> **Phiên bản:** 2.0 (Toàn diện: Backend ASP.NET Core, Mô phỏng Web3D Vật lý, App Quản trị WPF Desktop)  
> **Định dạng:** Văn bản thuần túy (Text-only, không chèn hình ảnh hay mã sơ đồ đồ họa)

---

## 1. TỔNG QUAN HỆ THỐNG & KIẾN TRÚC CÁC PHÂN HỆ

Hệ thống **BSS** vận hành dựa trên sự kết hợp đồng bộ giữa 4 phân hệ chính:

### 1.1 Phân hệ Trạm Đổi Pin / Kiosk Mô Phỏng (Web3D & Kiosk Simulator)
- **Công nghệ:** HTML5, CSS3, JavaScript (Three.js, Web Worker, MQTT.js).
- **Chức năng:**
  - Mô phỏng tủ trạm 3D thời gian thực với 8 ngăn chứa pin.
  - Tích hợp giao diện màn hình cảm ứng Kiosk dành cho tài xế xe điện (Driver).
  - Mô phỏng động học BMS của từng viên pin: mức pin (SoC), độ chai pin (SoH), nhiệt độ tăng/giảm theo dòng sạc, điện áp, đóng/mở cửa tủ, rút/cắm pin.
  - Sử dụng luồng **Web Worker chạy ngầm** để phát gói tin Telemetry đều đặn mỗi 1 giây (1 Hz) lên MQTT Broker, không bao giờ bị trình duyệt tạm dừng khi ẩn tab.

### 1.2 Phân hệ Máy Chủ Trung Tâm (BSS Web API & Core Engine)
- **Công nghệ:** C# (.NET 8), ASP.NET Core, MQTTnet, SQL Server.
- **Chức năng:**
  - Cung cấp cổng giao tiếp RESTful API xác thực JWT cho người dùng và thiết bị.
  - Tích hợp dịch vụ nền **MQTT Worker** lắng nghe và điều phối thông điệp IoT hai chiều giữa máy chủ và các trạm pin.
  - Lõi điều phối [BssEngine.cs](file:///d:/KTPMUD/BatterySwappingSystem/src/BatterySwapping.Core/BssEngine.cs) quản trị toàn bộ máy trạng thái phiên đổi pin, cấp quyền trạm (Lease), kiểm soát an toàn nhiệt và giao dịch ví tiền.

### 1.3 Phân hệ Quản Trị & Giám Sát (WPF Desktop App)
- **Công nghệ:** C# (.NET 8), WPF (Windows Presentation Foundation), MVVM, Material Design.
- **Chức năng:**
  - Dành riêng cho **Quản trị viên (Admin)** và **Kỹ thuật viên (Technician)**.
  - Giám sát trạng thái Telemetry thời gian thực của các trạm (đèn báo Online/Offline có bộ lọc chống nhấp nháy).
  - Hiển thị trực quan 8 ngăn tủ sạc: mức pin, nhiệt độ, cảnh báo bảo vệ, tình trạng cửa.
  - Tiếp nhận và xử lý sự cố quá nhiệt: xác nhận reset khẩn cấp cho các ngăn sạc bị quá nhiệt từ 3 lần trở lên.
  - Theo dõi KPI mạng lưới: doanh thu ngày, tổng lượt đổi pin, số pin sẵn sàng, báo cáo đối soát.

### 1.4 Phân hệ Lưu Trữ Dữ Liệu (SQL Server Database)
- **Cơ sở dữ liệu:** `BatterySwappingDB`.
- **Chức năng:** Lưu trữ bền vững tài khoản, ví khách hàng, danh mục pin, ngăn tủ, trạm pin, lịch sử đổi pin, phiên đổi pin, danh sách lệnh IoT và bảng quản lý khóa cách ly nhiệt (`BssSlotCooldowns`).

---

## 2. CÁC VAI TRÒ & PHÂN QUYỀN TRONG HỆ THỐNG

### Vai trò 1: Khách hàng / Tài xế xe điện (Driver)
- **Giao diện thao tác:** Màn hình Kiosk tại trạm (hoặc màn hình Kiosk trên Web3D).
- **Hành động được phép:**
  - Đăng nhập tài khoản bằng tên đăng nhập và mật khẩu.
  - Xem thông tin số dư ví tiền hiện tại và mã pin đang thuê mang theo xe.
  - Bấm nút khởi tạo yêu cầu đổi pin tự động.
  - Nạp tiền tự động vào ví trực tiếp tại Kiosk.
  - Thực hiện các thao tác vật lý: cắm pin cũ vào ngăn mở sẵn, rút pin mới được cấp.

### Vai trò 2: Kỹ thuật viên trạm (Technician)
- **Giao diện thao tác:** Ứng dụng Desktop WPF.
- **Hành động được phép:**
  - Giám sát chi tiết các thông số điện áp, dòng sạc, nhiệt độ từng ngăn tủ.
  - Xử lý các ngăn tủ báo lỗi, pin chai dưới 80% SoH hoặc pin bị ngắt sạc.
  - Thực hiện lệnh bảo trì thay thế pin mới vào ngăn tủ (`REPLACE_BATTERY`).
  - Mở khóa ngăn tủ thủ công khi có yêu cầu hỗ trợ từ hệ thống.

### Vai trò 3: Quản trị viên hệ thống (Admin)
- **Giao diện thao tác:** Ứng dụng Desktop WPF.
- **Hành động được phép:**
  - Toàn quyền của Kỹ thuật viên.
  - Phê duyệt và xác nhận mở khóa an toàn cho các ngăn sạc bị quá nhiệt từ 3 lần trở lên (`RESET_ALARM` với cờ Admin).
  - Quản lý danh sách trạm pin, thêm trạm, ngắt dừng hoạt động toàn trạm (`CUT_OFF` / `RESTORE_STATION`).
  - Quản lý giá dịch vụ đổi pin, xem báo cáo doanh thu và lịch sử giao dịch.

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
  3. Tài xế không có phiên đổi pin nào khác đang mở dở dang.
  4. Tài xế đang sở hữu 1 viên pin hợp lệ (đúng loại pin hệ thống hỗ trợ).
  5. Số dư ví khả dụng (`SoDuVi - Hold`) phải lớn hơn hoặc bằng phí dịch vụ (mặc định 15.000 đ).
  6. Trạm phải có ít nhất **1 ngăn trống hợp lệ** (không khóa bảo trì, cửa đang đóng, không trong đợt cách ly nhiệt).
  7. Trạm phải có ít nhất **1 viên pin đạt chuẩn cấp** (SoC $\ge 90\%$, SoH $\ge 80\%$, Nhiệt độ $\le 45^\circ\text{C}$, không bị khóa bảo trì).

#### Bước 2: Tạm giữ phí & Mở ngăn nhận pin cũ
- Hệ thống thực hiện lệnh giữ chỗ:
  - Tạm giữ khoản phí 15.000 đ trong ví tài xế (chưa trừ tiền thực tế).
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
  - Nhiệt độ pin cũ phải nằm trong ngưỡng kiểm soát ($< 55^\circ\text{C}$).
- **Xử lý tài chính và kho:**
  - Thu hồi quyền giữ pin cũ khỏi tài khoản tài xế (`MaPinDangMuon = NULL`).
  - Gán viên pin cũ vào ngăn tủ trạm và bắt đầu kích hoạt sạc lại.
  - **CHÍNH THỨC TRỪ TIỀN VÍ:** Trừ 15.000 đ từ số dư ví khách hàng.
  - Tạo bản ghi giao dịch trong bảng `LichSuDoiPin` ở trạng thái `DangXuLy`.
- Chuyển trạng thái phiên đổi pin sang `Dispensing`.

#### Bước 5: Mở ngăn cấp pin mới
- Hệ thống gửi lệnh MQTT: `EJECT_BATTERY` tới ngăn chứa pin mới (`ReceiveSlot`).
- Chốt điện ngăn mở ra, cơ cấu đẩy pin hơi nhô viên pin đầy ra ngoài.
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

### QUY TRÌNH 2: NẠP TIỀN VÍ VÀ ĐỐI SOÁT TÀI CHÍNH

1. **Nạp tiền ví tự động:**
   - Tài xế nhập số tiền cần nạp tại giao diện Kiosk (hoặc Web Portal).
   - Hệ thống tiếp nhận yêu cầu `POST /api/v1/wallet/deposit`.
   - Kiểm tra số tiền hợp lệ ($> 0$), tự động ghi nhận thanh toán thành công và cộng ngay lập tức vào `SoDuVi` của khách hàng trong cơ sở dữ liệu.
   - Tài xế có thể dùng số dư mới nạp để thực hiện đổi pin ngay lập tức mà không phải chờ duyệt thủ công.

2. **Cơ chế an toàn tạm giữ (Hold) vs Trừ tiền (Deduct):**
   - Khi bấm bắt đầu đổi pin: Số tiền chỉ bị đánh dấu tạm giữ (`Hold`), số dư khả dụng giảm nhưng tiền trong ví chưa mất.
   - Nếu xảy ra sự cố (hết thời gian chờ, khách bấm hủy trước khi đưa pin cũ vào): Lệnh giữ tiền tự động hủy bỏ, khách hàng không bị mất tiền.
   - Chỉ khi pin cũ đã vào ngăn an toàn, cửa đã đóng và hệ thống sẵn sàng nhả pin mới: Khoản phí mới bị trừ dứt điểm.

---

### QUY TRÌNH 3: BẢO VỆ AN TOÀN NHIỆT & XỬ LÝ QUÁ NHIỆT 3 CẤP ĐỘ

Nhiệt độ các khối pin lithium trong quá trình sạc được giám sát nghiêm ngặt qua 3 cấp độ:

#### 1. Các ngưỡng nhiệt độ quy chuẩn:
- **Dưới 45.0°C (Mức bình thường):** Pin được sạc dòng điện tiêu chuẩn (15A - 30A), đủ điều kiện cấp cho khách hàng nếu mức sạc đạt từ 90% trở lên.
- **Từ 45.0°C đến dưới 55.0°C (Mức cảnh báo):** Hệ thống BMS tự động giảm dòng sạc để hạ nhiệt; ngăn này tạm thời không được đưa vào danh sách cấp cho khách.
- **Từ 55.0°C trở lên (Mức sự cố quá nhiệt):** Kích hoạt lập tức quy trình cách ly khẩn cấp.

#### 2. Quy trình kích hoạt cách ly khẩn cấp:
- Ngay khi cảm biến phát hiện nhiệt độ $\ge 55^\circ\text{C}$:
  - Lập tức ngắt dòng sạc về 0A (`DongDien = 0`).
  - Khóa chốt cơ điện của ngăn tủ (`KhoaBaoTri = 1`), ngăn không cho mở cửa.
  - Ghi nhận đợt sự cố vào bảng `BssSlotCooldowns` với mốc thời gian: `UnlockAfter = Thời điểm hiện tại + 5 phút`.
  - Tăng bộ đếm số lần sự cố (`IncidentCount = IncidentCount + 1`).

#### 3. Cơ chế phục hồi 3 cấp độ:
- **Cấp độ 1 & 2 (Bị quá nhiệt lần 1 hoặc lần 2):**
  - Ngăn tủ được kích hoạt quạt làm mát tự động.
  - Bắt buộc phải chờ đủ **5 phút thời gian thực** (đồng hồ thực, không tăng tốc theo mô phỏng).
  - Bắt buộc nhiệt độ đo được phải hạ xuống **dưới 35.0°C**.
  - Khi thỏa mãn đồng thời 2 điều kiện trên, hệ thống cho phép phát lệnh `AUTO_RESET_ALARM` để mở khóa ngăn tủ và tiếp tục sạc bình thường. Không cần con người can thiệp.
- **Cấp độ 3 (Bị quá nhiệt từ lần 3 trở lên):**
  - Hệ thống tự động đánh dấu cờ: `RequiresAdmin = 1`.
  - Dù đã hết 5 phút và nhiệt độ đã hạ dưới 35.0°C, ngăn tủ **vẫn tiếp tục bị khóa cứng tuyệt đối**.
  - Web3D và màn hình Kiosk **không có nút bấm hay quyền** mở khóa ngăn này.
  - **Bắt buộc Quản trị viên (Admin):** Phải mở ứng dụng Desktop WPF, kiểm tra trực tiếp tình trạng ngăn tủ, sau đó bấm nút **"⚠️ Xác nhận Reset Quá Nhiệt (Admin)"**.
  - Ứng dụng WPF sẽ gửi lệnh xác thực quyền Admin lên máy chủ để xóa bỏ khóa cách ly, đưa `IncidentCount` về 0 và đưa ngăn tủ hoạt động trở lại.

---

### QUY TRÌNH 4: ĐỒNG BỘ IOT HAI CHIỀU & CHỐNG NGHẼN TRÌNH DUYỆT

Để đảm bảo kết nối giữa trạm giả lập Web3D và ứng dụng Quản trị WPF luôn ổn định và không bị chập chờn:

1. **Chu kỳ gửi Telemetry 1 Giây:**
   - Mỗi giây, trạm mô phỏng gửi 1 gói tin định dạng JSON qua giao thức MQTT tới chủ đề (topic): `bss/station/{Mã_Trạm}/telemetry`.
   - Gói tin chứa đầy đủ thông số 8 ngăn: mức pin, độ chai, nhiệt độ, điện áp, dòng điện, trạng thái cửa, trạng thái khóa.
2. **Cơ chế Web Worker chống trình duyệt bóp nghẽn:**
   - Khi người dùng chuyển sang làm việc trên cửa sổ ứng dụng WPF, trình duyệt (Chrome/Edge) sẽ đưa tab Web3D vào chế độ chạy ngầm và tự động bóp nghẽn các hàm `setInterval` thông thường từ 1 giây thành 10 - 60 giây một lần.
   - Hệ thống sử dụng một luồng **Web Worker riêng biệt** để điều khiển nhịp phát tin. Do chạy trên luồng tiến trình độc lập, Web Worker không bao giờ bị trình duyệt bóp nghẽn, đảm bảo dữ liệu luôn được bắn lên máy chủ đều đặn mỗi 1.000 ms.
3. **Cơ chế Timeout 5 Giây trên Backend:**
   - Backend theo dõi cột `LastSeen` của từng trạm trong cơ sở dữ liệu.
   - Nếu trong 5.0 giây không nhận được gói tin, máy chủ đánh dấu trạm là `Offline`.
   - Nhờ có Web Worker phát tin liên tục, thời gian trễ giữa các gói tin luôn duy trì ở mức $\le 1$ giây, loại bỏ hoàn toàn việc trạm bị chuyển sang trạng thái Offline sai lệch.
4. **Bộ lọc Debouncing trên App WPF:**
   - Ứng dụng WPF định kỳ thăm dò trạng thái trạm mỗi 2 giây.
   - Giao diện áp dụng bộ đếm: yêu cầu mất tín hiệu liên tiếp từ 2 chu kỳ (tối thiểu 4 giây) thì mới hiển thị nhãn đỏ `● THIẾT BỊ OFFLINE`. Nhờ đó, giao diện không bao giờ bị nhấp nháy giật màu khi mạng có độ trễ ngắn tích tắc.

---

## 4. BẢNG MÔ HÌNH DỮ LIỆU CỐT LÕI (DATABASE SCHEMAS)

Hệ thống quản lý dữ liệu thông qua các bảng quan hệ chính sau:

### Bảng 1: `KhachHang` (Thông tin tài xế & Ví tiền)
- `MaKH` (Khóa chính): Mã định danh duy nhất của khách hàng.
- `MaTK` (Khóa ngoại): Liên kết tới bảng tài khoản người dùng (`TaiKhoan`).
- `HoTen`: Họ tên khách hàng.
- `SoDuVi`: Số dư khả dụng trong ví tiền (dùng để trừ phí đổi pin).
- `MaPinDangMuon` (Khóa ngoại): Mã viên pin mà khách hàng hiện đang giữ trên xe.

### Bảng 2: `Pin` (Danh mục pin lưu hành)
- `MaPin` (Khóa chính): Mã định danh viên pin (ví dụ: `PIN-001`).
- `LoaiPin`: Loại chuẩn kết nối pin.
- `SoC`: Dung lượng pin hiện tại (0% - 100%).
- `SoH`: Sức khỏe / Độ chai của pin (0% - 100%). Pin dưới 80% sẽ bị đưa vào diện cần thu hồi.
- `NhietDo`: Nhiệt độ tế bào pin hiện tại (°C).
- `TrangThai`: Tình trạng pin (`DaDay`, `DangSac`, `DangSuDung`, `DaThuHoi`, `LoiQuaNhiet`).

### Bảng 3: `NganTu` (Các ngăn sạc tại trạm pin)
- `MaTram` & `SoThuTu` (Khóa chính phức hợp): Xác định ngăn số mấy tại trạm nào.
- `MaPinHienTai` (Khóa ngoại): Mã viên pin đang nằm trong ngăn (NULL nếu ngăn trống).
- `CuaDangMo`: Trạng thái cửa ngăn tủ (0: Đóng, 1: Mở).
- `KhoaBaoTri`: Trạng thái chốt khóa điện (0: Mở khóa hoạt động, 1: Khóa cách ly an toàn).
- `TrangThaiNgan`: Tình trạng ngăn tủ (`Trong`, `CoPin`, `DangSac`, `Loi`).

### Bảng 4: `BssSlotCooldowns` (Quản lý cách ly sự cố nhiệt)
- `StationId` & `SlotNumber` (Khóa chính): Ngăn tủ bị sự cố tại trạm.
- `BatteryId`: Mã viên pin bị quá nhiệt tại thời điểm xảy ra sự cố.
- `StartedAt`: Thời điểm bắt đầu ghi nhận quá nhiệt.
- `UnlockAfter`: Thời điểm sớm nhất được phép mở khóa (bắt buộc $\ge \text{StartedAt} + 5\text{ phút}$).
- `IncidentCount`: Tổng số lần ngăn tủ này bị quá nhiệt lũy kế.
- `RequiresAdmin`: Cờ đánh dấu yêu cầu quyền Admin (bật thành 1 khi `IncidentCount` $\ge 3$).
- `ClearedAt`: Thời điểm sự cố được xử lý và đóng thành công (NULL nếu đang bị khóa).

### Bảng 5: `LichSuDoiPin` (Đối soát giao dịch đổi pin)
- `MaGD` (Khóa chính): Mã giao dịch.
- `MaKH`, `MaTram`: Khách hàng và trạm thực hiện.
- `MaPinTra`, `SoC_PinTra`, `NganTra`: Thông tin chi tiết pin cũ được trả lại.
- `MaPinNhan`, `SoC_PinNhan`, `NganNhan`: Thông tin pin mới được cấp.
- `PhiDoiPin`: Số tiền dịch vụ đã thu.
- `TrangThai`: Tình trạng giao dịch (`DangXuLy`, `HoanThanh`, `ThatBai`).
- `ThoiGianBatDau`, `ThoiGianKetThuc`: Mốc thời gian thực hiện.

---

## 5. DANH MỤC LỆNH ĐIỀU KHIỂN THIẾT BỊ (IOT COMMAND CODES)

Khi máy chủ cần điều khiển phần cứng của trạm, các lệnh sau sẽ được đẩy xuống hàng đợi MQTT:

1. **`UNLOCK_SLOT`:** Mở chốt khóa cơ điện của ngăn tủ để cửa có thể mở ra (dùng khi mở ngăn cho khách trả pin).
2. **`CLOSE_SLOT`:** Yêu cầu trạm xác nhận đóng cửa ngăn tủ.
3. **`EJECT_BATTERY`:** Đẩy pin mới ra ngoài cho khách hàng rút (kèm mở khóa và nháy đèn LED định vị).
4. **`RETURN_OLD_BATTERY`:** Mở ngăn trả lại pin cũ cho khách hàng trong tình huống phiên đổi pin bị hủy bỏ trước khi cấp pin mới.
5. **`CUT_OFF`:** Lệnh khẩn cấp: lập tức ngắt toàn bộ dòng sạc của tất cả các ngăn về 0A và khóa toàn bộ trạm pin.
6. **`RESTORE_STATION`:** Khôi phục trạm pin hoạt động trở lại sau khi đã hoàn tất bảo trì hoặc kiểm tra an toàn toàn trạm.
7. **`AUTO_RESET_ALARM`:** Lệnh tự động xóa cảnh báo nhiệt độ và mở khóa ngăn tủ (chỉ áp dụng cho quá nhiệt lần 1 và lần 2 khi đã đủ 5 phút và dưới 35°C).
8. **`RESET_ALARM`:** Lệnh của Quản trị viên (Admin) gửi từ App WPF để xóa cảnh báo quá nhiệt lần 3 trở lên sau khi đã kiểm tra an toàn.
9. **`REPLACE_BATTERY`:** Lệnh của Kỹ thuật viên dùng để thay thế pin hỏng/pin chai tại ngăn tủ bằng một viên pin mới đạt chuẩn.

---

## 6. KẾT LUẬN & ĐIỂM NỔI BẬT CỦA HỆ THỐNG

1. **Quy trình chặt chẽ, khép kín:** Từng bước trong phiên đổi pin đều có điều kiện ràng buộc (Pre-conditions) và hậu điều kiện (Post-conditions) bảo đảm nguyên tắc "nhận pin cũ hợp lệ rồi mới trừ tiền và nhả pin mới".
2. **An toàn cháy nổ chủ động:** Thuật toán khóa nhiệt 3 cấp độ với thời gian thực 5 phút và điều kiện kép (< 35°C) ngăn chặn hoàn toàn nguy cơ sạc lại pin khi đang có nguy cơ thoát nhiệt (thermal runaway).
3. **Độ ổn định cao trong môi trường thực tế:** Việc kết hợp Web Worker ngầm, cơ chế chống lặp Idempotency và bộ lọc chống rung lắc giao diện giúp hệ thống vận hành mượt mà, sẵn sàng triển khai trên phần cứng thực tế.
