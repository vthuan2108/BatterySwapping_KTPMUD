# Quy trình BSS sau chuẩn hóa nghiệp vụ

## Phân quyền và xác thực

Đăng nhập tài khoản/mật khẩu qua `POST /api/v1/auth/login`. QR/RFID chỉ nhận diện sau đăng nhập; không tự chuyển tài khoản. `/auth/kiosk`, `/wallet/topup`, `/wallet/self-topup` và `/swaps/{id}/events` đã bị bỏ. WPF/Avalonia theo dõi phiên, thao tác vật lý chỉ thực hiện ở bảng demo thiết bị Web3D qua MQTT/lease.

## Khách mới và tài sản

Admin vào **Hồ sơ, kho và thanh toán** tạo hồ sơ: ví 0, chưa giữ pin. Chọn khách chưa giữ pin và pin kho đã có, nhập ghi chú rồi xác nhận bàn giao thực tế. Không cấp pin trong tủ, đang giữ chỗ, cách ly hoặc không đủ BMS. Bàn giao được lưu vết và chống gửi lặp.

`GET /inventory` suy ra vị trí từ người giữ, ngăn và khu kho/cách ly. `GET /consistency` báo pin nhiều chủ, vừa ở tủ vừa thuộc khách, hồ sơ thiếu tài khoản và ngược lại. Dữ liệu bất nhất bị chặn sử dụng, không tự ghi đè. Trước migration, server xuất báo cáo `inventory-consistency.json` bên cạnh file cấu hình trong `.runtime/` nếu phát hiện bất nhất.

Seed trước đây gán PIN-009 cho hai khách. Seed mới để khách thứ tư chưa có pin. Database đã cài không tự được sửa: admin phải đối chiếu thực tế và xác nhận chủ giữ pin; sao lưu và ghi lại quyết định trước khi sửa dữ liệu bằng công cụ SQL. Không chạy lại seed để sửa database đang dùng.

## Thanh toán giả lập

`POST /payments {amount,requestId,customerId?}` tạo Pending, chưa tăng ví. CustomerId chỉ admin được chọn; khách dùng hồ sơ của token. `GET /payments` trả danh sách cá nhân hoặc toàn bộ cho admin.

Admin vào tab Cổng thanh toán giả lập, chọn yêu cầu Pending và kết quả. `POST /payments/{id}/result {result}` nhận `Succeeded`, `Failed`, `Cancelled`. Kết quả cuối bất biến; Succeeded cộng ví và ghi lịch sử trong cùng transaction SQL, gọi lại không cộng thêm. Không có chuyển tiền ngân hàng.

`GET /policy`, `PUT /policy {fee,supportedBatteryType}` (ghi chỉ admin); `/pricing` là API đọc tương đương. Phiên đổi pin lưu phí lúc tạo, không thay đổi theo chính sách mới.

## Đổi pin và trả lại pin cũ

Đăng nhập → chọn trạm/đọc phí → giữ chỗ → MQTT xác nhận trả pin đúng và cửa đóng → kiểm tra BMS → thu phí một lần → lệnh cấp pin → MQTT xác nhận lấy pin/cửa đóng → hoàn tất.

Nhận pin cũ là chuyển quyền lưu giữ vào tủ ngay cả khi chưa thu phí. Hủy trước khi nhận pin chỉ giải phóng sau ACK đóng ngăn. Sau khi nhận pin, lỗi giữ tài sản và phiên ở NeedsAssistance.

Kỹ thuật viên dùng `POST /swaps/{id}/resume` cho phiên đã thu phí. Với phiên chưa thu phí, `POST /swaps/{id}/return-old` gửi RETURN_OLD_BATTERY; chỉ sự kiện OLD_RETURNED đúng pin/ngăn và cửa đóng mới trả quyền giữ pin, kết thúc Cancelled không thu phí. Mất kết nối chưa xác nhận không tự hoàn tiền hoặc gửi nhả pin lần hai.

## Bảo trì và thay pin

`POST /maintenance {stationId,slotNumber,action,newBatteryId,note,requestId}` với RESET_ALARM hoặc REPLACE_BATTERY trả202 cùng mã phiếu và commandId. `GET /maintenance` trả lịch sử. Theo dõi `/commands/{id}`; chưa nhận phản hồi không được hiểu là hoàn thành.

RESET_ALARM bắt đầu làm mát theo thời gian ở simulator; ACK thành công chỉ khi pin dưới35°C, cửa đóng, khóa được nhả. Không tăng SoH. Thay pin giữ chỗ pin kho đã đăng ký, yêu cầu pin kho dưới35°C, chờ thao tác mô phỏng tháo/lắp rồi ACK. Dùng BMS thực tế của pin đăng ký; pin tháo chuyển cách ly.

Phiếu Failed vẫn giữ khóa để đối soát. `POST /maintenance/{id}/retry` chỉ cho gửi lại sau phản hồi Failed rõ ràng, giữ nguyên tài sản và phiếu. Sent/Unconfirmed dùng truy vấn trạng thái, không gửi thao tác lại. Không tự nhả pin kho hoặc mở trạm khi còn phiếu chưa kết thúc.

## Migration và kiểm chứng

Backend chạy06 →07 →08 tăng dần, không drop/seed dữ liệu ứng dụng.07 bổ sung chính sách, vị trí, bàn giao, yêu cầu thanh toán;08 bổ sung phiếu và giữ chỗ bảo trì. Các bản ghi lịch sử cũ giữ nguyên. Chính sách mặc định loại pin khớp seed mới `Lithium LFP 72V-30Ah`; admin kiểm tra loại pin thực tế trong database trước khi đổi pin.

Chạy `scripts/Test-Integration.ps1` trên database riêng để kiểm tra API, MQTT, khởi động lại server, ví và bàn giao. Kiểm thử JS có `bms.test.js` và `controller.test.js`. XAML/điều hướng WPF có UiTests. Chưa nghiệm thu trực tiếp Avalonia/Ubuntu; build trên Windows không thay thế kiểm chứng đó. Chưa chạy toàn bộ kịch bản WebGL trực quan hoặc ngắt Internet cấp hệ điều hành.

## Kết quả kiểm tra của đợt chỉnh nghiệp vụ

- Solution Release: 0 lỗi, 0 cảnh báo, gồm WPF và Avalonia.
- 36 ca tích hợp API/MQTT/SQL trên database riêng, có restart thực tế trước/sau nhận pin và trả pin cũ.
- 21 kiểm tra mật khẩu, tạo hồ sơ, thanh toán bất biến/chống lặp và bàn giao đồng thời.
- Kiểm tra XAML/menu/workspace WPF đạt; đã mở màn hình admin fixture và phát hiện/sửa thông báo cảnh báo cố định sai. Chưa đi hết các thao tác giao diện của ba vai trò.
- Kiểm thử BMS và bộ điều khiển JavaScript đạt; chưa nghiệm thu WebGL trực quan.
- Chưa chạy trực tiếp Ubuntu hoặc thử ngắt Internet hệ điều hành; không coi build thành nghiệm thu hai mục này.
