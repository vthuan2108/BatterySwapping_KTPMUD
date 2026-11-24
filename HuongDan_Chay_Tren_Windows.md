# HƯỚNG DẪN CHẠY VÀ DEMO DỰ ÁN TRÊN WINDOWS
## ĐỀ TÀI: HỆ THỐNG QUẢN LÝ MẠNG LƯỚI TỦ ĐỔI PIN TỰ ĐỘNG CHO XE MÁY ĐIỆN (BSS)
**Học phần:** Kỹ thuật Phần mềm Ứng dụng (KTPMUD) - ĐHBK Hà Nội  
**Nền tảng:** C# .NET 8.0 / WPF (Windows Presentation Foundation)  
**Cơ sở dữ liệu:** Microsoft SQL Server  

---

## 1. YÊU CẦU MÔI TRƯỜNG TRÊN WINDOWS
Trước khi chạy, hãy đảm bảo máy tính Windows của bạn đã cài đặt:
1. **.NET 8.0 SDK** (Tải miễn phí tại: https://dotnet.microsoft.com/download/dotnet/8.0).
2. **Microsoft SQL Server** (Bản Developer hoặc Express) kèm công cụ **SSMS (SQL Server Management Studio)**.
3. **Visual Studio 2022 Community** (Tích hợp sẵn gói *.NET Desktop Development*) **HOẶC** **Visual Studio Code** (Kèm Extension *C# Dev Kit*).

---

## 2. BƯỚC 1: KHỞI TẠO CƠ SỞ DỮ LIỆU (SQL SERVER)

1. Mở phần mềm **SQL Server Management Studio (SSMS)** và kết nối vào SQL Server của bạn (thường là `localhost`, `.\SQLEXPRESS` hoặc tên máy tính).
2. Mở và thực thi (Execute / `F5`) **lần lượt theo thứ tự** 3 file script nằm trong thư mục `Database/`:
   * **`01_CreateDatabaseAndTables.sql`**: Khởi tạo CSDL `BatterySwappingDB` và 8 bảng quan hệ chuẩn hóa.
   * **`02_TriggersAndProcedures.sql`**: Tạo Trigger tự động ngắt sạc chống cháy nổ và các Stored Procedure đổi pin an toàn.
   * **`03_SampleSeedData.sql`**: Nạp sẵn dữ liệu mẫu (1 trạm 8 ngăn, 12 viên pin, 3 khách hàng, tài khoản demo).
3. Sau khi chạy xong, kiểm tra thấy thông báo: `>>> ĐÃ KHỞI TẠO DỮ LIỆU MẪU CHO CSDL BATTERYSWAPPINGDB THÀNH CÔNG! <<<`.

> [!NOTE]
> Hệ thống đã được tích hợp tính năng **Tự động nhận diện Instance SQL Server** (`.\SQLEXPRESS` hoặc `localhost`).  
> Ứng dụng sẽ tự động dò tìm và kết nối thẳng vào database `BatterySwappingDB` mà bạn không cần phải cấu hình thủ công.  
> Nếu muốn tùy biến, bạn có thể chỉnh sửa chuỗi kết nối ngay tại ô cấu hình dưới chân cửa sổ Đăng nhập khi khởi chạy!

---

## 3. BƯỚC 2: CHẠY DỰ ÁN TRÊN WINDOWS

### Cách 1: Sử dụng Visual Studio 2022 (Khuyên Dùng Tốt Nhất)
1. Nhấp đúp vào file solution **`BatterySwappingSystem.sln`** để mở toàn bộ dự án trong Visual Studio.
2. Để chạy **cả 2 ứng dụng song song** (Vừa có màn hình Kiosk/Admin, vừa có bộ Simulator điều khiển phần cứng):
   * Trong cửa sổ *Solution Explorer*, nhấp chuột phải vào tên Solution `BatterySwappingSystem` $\to$ Chọn **Properties**.
   * Chọn mục **Startup Project** ở cột bên trái $\to$ Tích chọn **Multiple startup projects**.
   * Đặt cả hai dự án là **Action = Start**:
     * `BatterySwapping.App` $\to$ **Start**
     * `BatterySwapping.Simulator` $\to$ **Start**
   * Nhấn **OK**.
3. Nhấn phím **F5** (hoặc nút **Start** màu xanh) để khởi động cả 2 ứng dụng lên màn hình!

### Cách 2: Sử dụng VS Code trên Windows qua Terminal
Mở thư mục `BatterySwappingSystem/` bằng VS Code, mở Terminal (`Ctrl + ~`) và chạy:

```powershell
# Chạy ứng dụng giao diện Kiosk/Admin
dotnet run --project src/BatterySwapping.App.Wpf/BatterySwapping.App.Wpf.csproj

# Mở thêm 1 tab Terminal mới và chạy bộ mô phỏng IoT Simulator:
dotnet run --project src/BatterySwapping.Simulator/BatterySwapping.Simulator.csproj
```

---

## 4. KỊCH BẢN DEMO THỰC CHIẾN (CHỤP ẢNH LÀM BÁO CÁO CHƯƠNG 3)

### Kịch bản 1: Demo chu trình đổi pin 6 bước tại Kiosk
1. Trên ứng dụng chính, bấm vào nút **"Mở Màn Hình Kiosk Tại Trạm"** (hoặc Menu `Màn hình Kiosk Tại Trạm`).
2. Chọn tài xế **Nguyễn Văn An** (SĐT: `0987654321` - Số dư ví: 150.000 đ).
3. Bấm **"TIẾP TỤC ĐỔI PIN"**:
   * **Bước 1:** Hệ thống kiểm tra số dư ví hợp lệ (150.000 đ $\ge$ 25.000 đ).
   * **Bước 2:** Hệ thống tự động tìm ngăn trống (Ngăn #6) $\to$ Phát lệnh mở cửa khay #6 để cắm pin cạn `PIN-009`.
   * **Bước 3:** Bấm nút *"Đã cắm pin và đóng cửa khay"* $\to$ Hệ thống đọc BMS pin cũ (Dung lượng 18%, SoH 95%).
   * **Bước 4:** Thuật toán tự động tìm pin tối ưu $\to$ Lựa chọn viên pin `PIN-001` (SoC 98.5%) tại ngăn #1.
   * **Bước 5:** Cửa ngăn #1 tự động mở $\to$ Bấm *"Đã lấy pin và đóng cửa tủ"*.
   * **Bước 6:** Hệ thống chốt giao dịch trong Transaction CSDL: Trừ 25.000 đ ví $\to$ Hiển thị **Biên lai điện tử thành công** (Số dư còn 125.000 đ).

### Kịch bản 2: Demo kiểm tra ràng buộc số dư không đủ
1. Tại màn hình Kiosk, chọn tài xế **Lê Hoàng Cường** (Số dư chỉ có 10.000 đ).
2. Bấm tiếp tục $\to$ Ứng dụng lập tức báo lỗi: *"Số dư ví không đủ để thực hiện đổi pin (yêu cầu tối thiểu 25.000 VNĐ)! Vui lòng nạp thêm tiền."* (Rất tốt để đưa vào Bảng Test Case).

### Kịch bản 3: Demo bộ mô phỏng IoT tương tác thời gian thực & Phòng cháy nổ (> 55°C)
*(Đây là kịch bản kỹ thuật ăn trọn điểm xuất sắc của thầy cô phản biện!)*
1. Đặt 2 cửa sổ `BatterySwapping.App` (tab Bảng điều khiển trạm) và `BatterySwapping.Simulator` nằm cạnh nhau trên màn hình.
2. Trên cửa sổ **Simulator**:
   * Chọn ngăn tủ **#4** (viên pin đang sạc).
   * Bấm nút màu đỏ: **"🔥 KÍCH HOẠT QUÁ NHIỆT (65°C)"**.
3. **Kết quả tức thì:**
   * Trigger trong SQL Server lập tức phát hiện nhiệt độ $\ge 55^\circ\text{C}$ và ghi cảnh báo nguy hiểm.
   * `SafetyMonitorService` trên App chính lập tức **bật còi báo động đỏ**, hiện thông báo khẩn cấp lên màn hình:  
     *"SỰ CỐ QUÁ NHIỆT PIN (65.0°C) - HỆ THỐNG ĐÃ TỰ ĐỘNG NGẮT SẠC VÀ KHÓA CHẶT TỦ CHỐNG CHÁY NỔ"*.
   * Trên sơ đồ 8 ngăn, ngăn #4 ngay lập tức chuyển sang **MÀU ĐỎ** và hiển thị trạng thái `NGUY HIỂM / KHÓA`.

### Kịch bản 4: Demo sạc pin tự động
1. Trên giao diện Simulator, tích chọn: **"⚡ Tự động sạc pin (+1% / 3s)"**.
2. Nhìn sang sơ đồ 8 ngăn trên App chính: Sau mỗi 3 giây, các viên pin đang cắm sạc (PIN-004, PIN-005) sẽ tự động nhảy phần trăm dung lượng tăng dần và thanh tiến trình sạc nhích dần lên!

### Kịch bản 5: Demo nạp tiền ví & Quản lý danh mục
1. Chuyển sang menu **Khách Hàng & Ví Tiền** $\to$ Bấm **"Nạp tiền vào ví"** $\to$ Chọn số tiền 100.000 đ $\to$ Xác nhận.
2. Chuyển sang menu **Lịch Sử Giao Dịch** $\to$ Xem toàn bộ bảng kê các lượt đổi pin và nạp tiền chi tiết.

### Kịch bản 6: Demo nghiệp vụ Kỹ thuật viên (KyThuatVien) - Kiểm định, bảo trì và thay thế pin
1. Đăng nhập với tài khoản Kỹ thuật viên: `tech01` / Mật khẩu: `123456`.
2. Hệ thống tự động chuyển sang chế độ Kỹ thuật viên với giao diện giám sát an toàn.
3. Chuyển sang menu **Quản Lý Danh Mục Pin**:
   * Nhấp đúp vào bất kỳ viên pin nào (hoặc chọn dòng rồi bấm **"🛠️ Kiểm định / Bảo trì"**).
   * Cửa sổ **Kiểm Định & Bảo Trì Thay Thế Pin** hiển thị đầy đủ thông số BMS (SoC, SoH, Nhiệt độ, Điện áp, Dòng sạc, Chu kỳ sạc xả) cùng kết luận chẩn đoán kỹ thuật.
   * Nếu phát hiện pin quá nhiệt hoặc pin chai (SoH < 80%): Kỹ thuật viên có thể bấm **"🛠️ Hoàn tất Bảo Dưỡng / Reset Lỗi Quá Nhiệt"** (đưa nhiệt độ về 25°C, mở khóa tủ) **HOẶC** nhập mã pin mới (ví dụ `PIN-015`) và bấm **"🔄 Thay Pin Mới (100%)"** để tự động thu hồi pin cũ, lắp pin mới vào khay và đóng cảnh báo tồn đọng trong CSDL.
4. Chuyển sang menu **Cảnh Báo An Toàn IoT**:
   * Bấm **"Đã khắc phục"** tại dòng sự cố: Hệ thống tự động ghi nhận họ tên kỹ thuật viên xử lý và mở khóa khay tủ tương ứng.

### Kịch bản 7: Demo Thêm trạm đổi pin mới vào mạng lưới (TC-07)
1. Tại tab **Bảng Điều Khiển Trạm**, bấm nút **"➕ Thêm Trạm"**.
2. Nhập thông tin trạm mới (Mã: `TRAM-DD03`, Tên: `Trạm Đổi Pin Đống Đa`, Địa chỉ: `Số 1 Đại Cồ Việt, Hai Bà Trưng, Hà Nội`, Số ngăn: `8`).
3. Bấm **"Tạo Trạm Mới"**: Hệ thống tự động khởi tạo trạm cùng 8 ngăn tủ trống trong cùng một Transaction an toàn (ACID) và cập nhật ngay vào danh sách trạm trên giao diện.

### Kịch bản 8: Demo Web 3D Digital Twin Simulator (Bản sao số 3D Tương tác)
1. Vào thư mục `src/BatterySwapping.Web3D/` $\to$ Nhấp đúp file **`run_web3d.bat`** (trên Windows) hoặc chạy `./run_web3d.sh` (trên Ubuntu).
2. Trình duyệt tự động mở giao diện 3D tại `http://localhost:5000`:
   - Dùng **Chuột trái** xoay tháp tủ 360°, **Chuột phải** lia vị trí, **Con lăn** phóng to xem từng chi tiết pin.
   - Thử click vào **Ngăn #01** $\to$ Bấm **"🚪 Mở Cửa Khay"** $\to$ Cánh cửa mở xoay mượt mà 85°.
   - Bấm **"🔋 Rút Pin Ra"** $\to$ Viên pin bay trượt ra ngoài khoang tủ, viền LED đổi sang màu xám.
   - Bấm **"🔋 Cắm Pin Vào"** $\to$ Viên pin bay vào cắm khít trong hộc tủ, viền LED chuyển sang màu vàng hổ phách và bắt đầu tự sạc.
   - Bấm nút đỏ: **"🔥 KÍCH HOẠT QUÁ NHIỆT (65°C)"** $\to$ Quan sát khói 3D bốc lên nghi ngút từ khe tản nhiệt, viền LED nhấp nháy đỏ báo động và cửa tủ tự động khóa chốt an toàn!
   - Bấm **"❄️ Hạ Nhiệt Khẩn Cấp"** để tắt khói và khôi phục hoạt động bình thường.

---

## 5. DANH SÁCH TÀI KHOẢN ĐĂNG NHẬP MẪU (MÃ HÓA SHA-256)
* **Admin (Quản trị):** `admin` | Mật khẩu: `123456` (Mã băm SHA-256: `8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92`)
* **Kỹ thuật viên:** `tech01` | Mật khẩu: `123456` (Tự động kích hoạt quyền kỹ thuật viên)
* **Tài xế 1:** `driver01` (SĐT: `0987654321`) | Mật khẩu: `123456`
* **Tài xế 2:** `driver02` (SĐT: `0912345678`) | Mật khẩu: `123456`
* **Cơ chế Password Migration:** Nếu mật khẩu tài khoản trong CSDL là plain-text, hệ thống sẽ tự động băm và nâng cấp sang SHA-256 ngay trong lần đăng nhập đầu tiên.
