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

Quá nhiệt từ55°C tự bật làm mát, ngắt sạc và khóa ngăn tối thiểu5 phút thực. Không chờ nút RESET_ALARM mới làm mát. Trong thời gian này cấm nhận/rút/cấp/thay pin và mở cửa; ngăn khác vẫn hoạt động nếu đủ an toàn. Hai đợt đầu dùng AUTO_RESET_ALARM tự động sau hết5 phút và dưới35°C; đợt thứ3 dùng RESET_ALARM với quyền Admin. ACK thành công chỉ khi cửa đóng và trạng thái an toàn phù hợp. Không tăng SoH. Thay pin giữ chỗ pin kho đã đăng ký, yêu cầu pin kho dưới35°C, chờ thao tác mô phỏng tháo/lắp rồi ACK. Dùng BMS thực tế của pin đăng ký; pin tháo chuyển cách ly.

Phiếu Failed vẫn giữ khóa để đối soát. `POST /maintenance/{id}/retry` chỉ cho gửi lại sau phản hồi Failed rõ ràng, giữ nguyên tài sản và phiếu. Sent/Unconfirmed dùng truy vấn trạng thái, không gửi thao tác lại. Không tự nhả pin kho hoặc mở trạm khi còn phiếu chưa kết thúc.

## Migration và kiểm chứng

Backend chạy06 →07 →08 →09 →10 tăng dần, không drop/seed dữ liệu ứng dụng.07 bổ sung chính sách, vị trí, bàn giao, yêu cầu thanh toán;08 bổ sung phiếu và giữ chỗ bảo trì;09 lưu thời điểm khóa nhiệt bền vững theo trạm/ngăn. Các bản ghi lịch sử cũ giữ nguyên. Chính sách mặc định loại pin khớp seed mới `Lithium LFP 72V-30Ah`; admin kiểm tra loại pin thực tế trong database trước khi đổi pin.

Chạy `scripts/Test-Integration.ps1` trên database riêng để kiểm tra API, MQTT, khởi động lại server, ví và bàn giao. Kiểm thử JS có `bms.test.js` và `controller.test.js`. XAML/điều hướng WPF có UiTests. Chưa nghiệm thu trực tiếp Avalonia/Ubuntu; build trên Windows không thay thế kiểm chứng đó. Chưa chạy toàn bộ kịch bản WebGL trực quan hoặc ngắt Internet cấp hệ điều hành.

## Kết quả kiểm tra của đợt chỉnh nghiệp vụ

- Solution Release: 0 lỗi, 0 cảnh báo, gồm WPF và Avalonia.
- 36 ca tích hợp API/MQTT/SQL trên database riêng, có restart thực tế trước/sau nhận pin và trả pin cũ.
- 21 kiểm tra mật khẩu, tạo hồ sơ, thanh toán bất biến/chống lặp và bàn giao đồng thời.
- Kiểm tra XAML/menu/workspace WPF đạt; đã mở màn hình admin fixture và phát hiện/sửa thông báo cảnh báo cố định sai. Chưa đi hết các thao tác giao diện của ba vai trò.
- Kiểm thử BMS và bộ điều khiển JavaScript đạt; chưa nghiệm thu WebGL trực quan.
- Chưa chạy trực tiếp Ubuntu hoặc thử ngắt Internet hệ điều hành; không coi build thành nghiệm thu hai mục này.

## Làm mát tự động và khóa nhiệt tối thiểu5 phút

- Ngưỡng kích hoạt: nhiệt độ >=55°C hoặc thiết bị báo cờ quá nhiệt đang giữ. Tự ngắt sạc và bật làm mát ngay tại simulator, kể cả mất backend.
- Backend lưu StartedAt/UnlockAfter/ClearedAt trong BssSlotCooldowns. Nhiệt độ còn cao trong cùng sự cố không đặt lại bộ đếm mỗi giây. Khi đã reset an toàn mà quá nhiệt trở lại, tạo mốc mới.
- Trong5 phút: không được mở ngăn, đưa pin vào, rút/cấp/thay pin hay khôi phục toàn trạm để vượt khóa. Hết giờ vẫn giữ khóa nếu còn nóng, offline hoặc chưa có ACK. Từ đợt thứ3 cần Admin xác nhận. Không tự đóng cảnh báo theo đồng hồ.
- Sau5 phút và dưới35°C: backend tự gửi AUTO_RESET_ALARM trong hai đợt đầu, không cần kỹ thuật viên/Admin bấm. Đợt thứ3 giữ khóa, Admin mới được tạo phiếu RESET_ALARM; kỹ thuật viên không được duyệt. ACK hợp lệ mới kết thúc cách ly. Ngắt toàn trạm thủ công vẫn giữ nguyên, không bị tự phục hồi nhiệt bỏ qua.
- Web3D lưu bộ đếm localStorage; tải lại trang lấy mốc muộn hơn giữa local và SQL. Backend khởi động lại không làm mất mốc. Nếu sự cố xảy ra lúc offline và đã nguội trước khi reconnect, cờ isOverheated giúp backend bắt đầu một khoảng khóa bảo thủ từ lúc quan sát được sự cố.
- Tốc độ vật lý x1/x10/x60 chỉ đổi tốc độ sạc/nhiệt, không giảm5 phút khóa. WPF/Avalonia/Web3D hiển thị thời gian còn lại và trạng thái chờ xác nhận.
- Migration09 không seed/xóa dữ liệu; pin đã quá nhiệt hoặc đang lỗi quá nhiệt khi nâng cấp được áp dụng tối thiểu5 phút mới theo hướng bảo thủ.

API đọc: GET /api/v1/stations/{id}/cooldowns trả SlotCooldown gồm stationId,slotNumber,batteryId,startedAt,unlockAfter,clearedAt. Snapshot thiết bị cũng chứa cooldowns. MQTT SlotTelemetry bổ sung isOverheated tùy chọn, mặc định false để tương thích payload cũ.

Để kiểm tra mà không dừng demo đang chạy:

```powershell
pwsh -File scripts/Test-Cooldown-Isolated.ps1
```

Script dùng database kiểm thử riêng, HTTP5082, MQTT18884 và WebSocket19002; không sửa cấu hình hay dừng server demo ở5080. Không chạy song song với bài tích hợp khác cùng database kiểm thử. Bài ranh giới dùng TimeProvider giả trong test và dịch mốc trên fixture có guard database; không có API tăng tốc thời gian khóa trong sản phẩm.

Kết quả đợt khóa nhiệt:40 ca tích hợp, kiểm tra mốc4:59/5:00, restart, khóa sau khi đã nguội, khóa khi hết giờ còn nóng, chống lệnh đã xếp hàng và giữ ngăn khác hoạt động đều đạt. WPF/Avalonia build0 lỗi/0 cảnh báo, UiTests và hai bộ JS đạt. Chưa nghiệm thu đồ họa WebGL hoặc Ubuntu trực tiếp.

## Bản chỉnh mô phỏng: phục hồi hai lần đầu, khóa Admin từ lần3

IncidentCount và RequiresAdmin được bổ sung bởi migration10. Mỗi đợt nóng mới sau đợt đã phục hồi tăng1; các telemetry đang nóng liên tục chỉ giữ cùng đợt. Đếm theo viên pin tại ngăn từ lần Admin xác nhận/reset kịch bản gần nhất; thay viên pin khác bắt đầu đếm mới. Hai lần tự phục hồi không xóa bộ đếm. Admin xác nhận an toàn sau đợt3 xóa đếm về0; đợt kế tiếp là lần1.

AUTO_RESET_ALARM gắn đúng mốc StartedAt và mã pin; lệnh cũ không được đóng sự cố mới. Nếu mất kết nối hoặc chưa nhận ACK vẫn giữ khóa. Khi trạm bị ngắt toàn bộ, phục hồi nhiệt không được tự bật lại sạc toàn trạm.

POST /api/v1/stations/{station}/simulation/reset là công cụ Admin, nhận requestId,clientId,leaseId của simulator đang giữ trạm. Không dùng để hủy giao dịch hoặc xóa tài sản. BMS ngẫu nhiên được lưu SQL cùng request chống lặp và đổi lease để telemetry cũ không ghi đè. Thông số random chỉ áp dụng pin có trong tủ trạm đó; pin bên ngoài, tài khoản, ví, lịch sử và cấu hình dung lượng không đổi. Cảnh báo của kịch bản cũ được đánh dấu xác nhận bằng reset giả lập, không xóa lịch sử.

Nếu reset chưa rõ kết quả, simulator tạm dừng gửi telemetry, lưu đúng request và tiếp tục đối soát sau tải lại. Không tự lấy lease mới trước khi xử lý xong yêu cầu cũ. Admin+đúng client/lease hiện tại có thể khôi phục yêu cầu chưa áp dụng dù heartbeat đã hết hạn; simulator khác đã tiếp quản sẽ bị chặn.

Sạc tính theo Ah và delta thời gian monotonic; tích phân từng giai đoạn dòng sạc để tab chạy nền lâu không làm mất thời gian. Điện áp/dòng là giá trị phụ thuộc SoC, không random độc lập. SoH chỉ random lúc reset kịch bản, không giảm từng giây.

Kiểm chứng:41 ca tích hợp;3 chu kỳ nhiệt và mốc4:59/5:00; reset chống lặp/mất phản hồi/quyền sở hữu; công thức sạc và ba bộ JS đều đạt. WPF/Avalonia build không lỗi/cảnh báo. Chưa nghiệm thu trực tiếp trên Ubuntu hoặc đi hết toàn bộ tương tác WebGL.

## Hiển thị màu sau chỉnh ngưỡng lần3

- Khóa làm mát tạm thời ở đợt1–2: nền, viền và LED vàng/cam, kèm nhãn LÀM MÁT / KHÓA TẠM. Dù dưới35°C nhưng chưa hết5 phút hoặc chưa có ACK thì vẫn vàng/cam, chưa được lấy pin.
- Đợt thứ3 đang cách ly: đỏ, nhãn LỖI NHIỆT / CHỜ ADMIN. Migration11 nâng cờ Admin của các đợt đang mở có đếm>=3, giữ nguyên thời gian bắt đầu và lịch sử.
- Sau phục hồi có xác nhận: bỏ màu cách ly, trở lại màu trạng thái pin (xanh nếu đủ SoC). Nhiệt độ số có màu cảnh báo riêng; khóa bảo trì thủ công không bị mô tả thành lỗi nhiệt.
- Hiệu ứng khói của mô phỏng dừng khi nhiệt độ xuống dưới55°C; cờ khóa không được dùng để giữ khói sau khi pin đã nguội.
