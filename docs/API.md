> **Cập nhật nghiệp vụ:** xem [hợp đồng và luồng mới](BUSINESS-WORKFLOWS.md). Các API cộng ví trực tiếp và đăng nhập định danh đã bị bỏ; dùng payments và đăng nhập mật khẩu.

# Hợp đồng đang triển khai — v1

Địa chỉ cục bộ `http://127.0.0.1:5080`. REST JSON dùng camelCase cho DTO; các kết quả SQL dictionary (`wallet`, `alerts`) giữ tên cột gốc. Client C# deserialize không phân biệt hoa thường. API bảo vệ yêu cầu `Authorization: Bearer <token>`; token trả từ đăng nhập. Thiết bị dùng lease riêng, không dùng token tài khoản.

| Method / URL | Request | Response |
|---|---|---|
| POST `/api/v1/auth/login` | `{username,password}` | 200 `{token,user:{id,username,name,role,customerId}}` |
| POST `/api/v1/auth/logout` | `{}` | 204; token bị thu hồi |
| GET `/api/v1/auth/me` | — | 200 user |
| GET `/api/v1/stations` | — | 200 danh sách TramPin |
| GET `/api/v1/stations/{id}/slots` | — | 200 danh sách NganTu cùng BMS |
| GET `/api/v1/stations/{id}/status` | — | 200 `{stationId,online,lastSeen,ownerId}`; ownerId không phải lease secret |
| POST `/api/v1/stations` | `{maTram,tenTram,diaChi,soLuongNgan,...}` | 200 trạm mới; Admin |
| GET/POST `/api/v1/batteries` | POST: `{maPin,loaiPin,dungLuongThietKe,soC,soH,nhietDo,...}` | GET danh sách; POST pin đã thêm; POST chỉ Admin |
| GET `/api/v1/customers` | — | Danh sách khách hàng; Admin |
| GET `/api/v1/wallet` | — | Khách hàng của tài khoản, gồm `SoDuVi`, `ReservedBalance` |
| POST `/api/v1/payments` | `{amount,requestId,customerId?}` |202 yêu cầu Pending; khách chỉ dùng ví mình, admin được chọn khách |
| GET `/api/v1/payments` | — | Yêu cầu cá nhân; admin xem toàn bộ |
| POST `/api/v1/payments/{id}/result` | `{result}` | Admin; Succeeded/Failed/Cancelled, kết quả cuối bất biến |
| GET `/api/v1/policy`, `/api/v1/pricing` | — | `{fee,supportedBatteryType}` |
| PUT `/api/v1/policy` | `{fee,supportedBatteryType}` | Admin; chỉ tác động phiên mới |
| POST `/api/v1/customers/create` | `{username,password,name,phone,email,licensePlate,requestId}` | Admin; hồ sơ ví0, chưa có pin |
| GET `/api/v1/inventory` | — | Vị trí tài sản và giữ chỗ, admin/kỹ thuật |
| GET `/api/v1/consistency` | — | Admin; bất nhất cần đối soát |
| POST `/api/v1/handovers` | `{customerId,batteryId,note,requestId}` | Admin; bàn giao ban đầu có chống lặp |
| GET `/api/v1/handovers` | — | Admin; lịch sử bàn giao |
| POST `/api/v1/maintenance` | `{stationId,slotNumber,action,newBatteryId,note,requestId}` |202 phiếu và commandId; admin/kỹ thuật |
| GET `/api/v1/maintenance` | — | Admin/kỹ thuật; lịch sử phiếu |
| POST `/api/v1/maintenance/{id}/retry` | `{}` |202; chỉ phiếu Failed có phản hồi rõ ràng |
| POST `/api/v1/swaps/{id}/return-old` | `{}` |202; trả pin cũ cho phiên chưa thu phí, admin/kỹ thuật |
| GET `/api/v1/me/history/swaps`, `/api/v1/me/history/topups` | — | Lịch sử của chính khách đăng nhập |
| POST `/api/v1/swaps` | `{stationId,idempotencyKey}` | 202 SwapSession; khách hàng sử dụng ví của chính mình |
| GET `/api/v1/swaps` | — | Danh sách phiên; khách hàng chỉ thấy phiên của mình |
| GET `/api/v1/swaps/{id}` | — | 200 SwapSession, kiểm tra chủ sở hữu |
| POST `/api/v1/swaps/{id}/cancel` | `{}` | Phiên đang đóng ngăn hoặc cần hỗ trợ; chưa có nghĩa hủy hoàn tất |
| POST `/api/v1/swaps/{id}/resume` | `{}` | Phiên đã thu phí được đối soát/tiếp tục; kỹ thuật/Admin |
| POST `/api/v1/stations/{id}/commands` | `{command,slotNumber?,batteryId?}` | 202 DeviceCommand và Location; kỹ thuật/Admin |
| GET `/api/v1/commands/{id}` | — | DeviceCommand, kỹ thuật/Admin |
| GET `/api/v1/commands` | — | Tối đa 200 lệnh gần nhất, kỹ thuật/Admin |
| GET `/api/v1/alerts` | — | Cảnh báo, kỹ thuật/Admin |
| GET `/api/v1/reports/summary` | — | Thống kê, Admin |
| GET `/api/v1/history/swaps` | — | Lịch sử đổi pin, Admin |
| GET `/api/v1/history/topups` | — | Lịch sử nạp ví, Admin |

Ví dụ response tạo phiên:

```json
{"id":"<sessionId>","customerId":1,"stationId":"TRAM-BK01","oldBatteryId":"PIN-009","newBatteryId":"PIN-001","returnSlot":6,"receiveSlot":1,"state":"AwaitingOldBattery","fee":25000,"charged":false,"oldInserted":false,"transactionId":null}
```

Lỗi nghiệp vụ: 409 `{error,code:"BUSINESS_RULE"}`. Chưa đăng nhập: 401. Không đúng quyền: 403. Không tồn tại: 404. JSON binding sai trả 400 theo ASP.NET. Một số lỗi database/môi trường trả 500 với thông báo chung, chi tiết chỉ ở log server. Không coi HTTP 202 là thiết bị đã thực hiện thành công.

## Giao thức thiết bị

`GET /api/v1/device/config`: cấu hình kết nối broker cục bộ. `POST /api/v1/device/lease {stationId,clientId}`: cấp `{leaseId,stationId,expiresAt}` nếu chưa có simulator sống. `GET /api/v1/device/{id}/snapshot`: trạng thái trạm, ngăn, pin, phiên hoạt động và lệnh để phục hồi; không trả lease secret. Các endpoint này chỉ được phục vụ ở loopback, không phải API công khai để triển khai Internet.

Broker TCP `127.0.0.1:18883`, WebSocket `ws://127.0.0.1:19001/mqtt`. Tài khoản simulator không được publish topic commands. Lease gắn với stationId; thông điệp sai lease, sai version, sai thời gian, sequence cũ, thiếu ngăn hoặc BMS ngoài giới hạn bị từ chối.

Các topic `bss/station/{stationId}/{telemetry|alerts|events|commands|acks}`. Telemetry QoS 0 mỗi giây. Các topic còn lại QoS 1; không retain commands.

```json
{"schemaVersion":1,"stationId":"TRAM-BK01","leaseId":"<private lease>","messageId":"<unique>","sequence":1,"timestamp":"<UTC ISO8601>","event":null,"commandId":null,"sessionId":null,"slotNumber":null,"batteryId":null,"success":true,"slots":[{"slotNumber":1,"batteryId":"PIN-001","soc":98.5,"soh":99,"temperature":31,"voltage":78,"current":0,"isCharging":false,"isDoorOpen":false,"isLocked":false}]}
```

Ví dụ trên rút gọn một phần tử; thông điệp thực tế phải có đúng toàn bộ các ngăn. `event` là `OLD_INSERTED`, `NEW_TAKEN`, `OVERHEAT` hoặc `ACK` tùy topic. ACK phải mang commandId, kết quả success và snapshot đầy đủ.

Lệnh:

```json
{"schemaVersion":1,"stationId":"TRAM-BK01","commandId":"<unique>","command":"EJECT_BATTERY","slotNumber":1,"batteryId":"PIN-001","sessionId":"<sessionId>","timestamp":"<UTC ISO8601>"}
```

Lệnh được hỗ trợ: UNLOCK_SLOT, CLOSE_SLOT, LOCK_SLOT, EJECT_BATTERY, CUT_OFF, RESTORE_STATION, RESET_ALARM, REPLACE_BATTERY, TRIGGER_OVERHEAT, QUERY_STATUS. Sau 10 giây thiếu ACK, lệnh thành Unconfirmed và gửi QUERY_STATUS. Không tự thực hiện lại thao tác nhả pin. QUERY_STATUS đối soát kết quả đã lưu tại simulator.

RESET_ALARM và REPLACE_BATTERY phải gửi qua phiếu bảo trì, không nhận từ API commands tổng quát. Sự kiện OLD_RETURNED xác nhận pin cũ được lấy và cửa đóng; phiên kết thúc Cancelled không thu phí.

## Khóa nhiệt

GET /api/v1/stations/{id}/cooldowns (đã đăng nhập) trả danh sách `{stationId,slotNumber,batteryId,startedAt,unlockAfter,clearedAt,incidentCount,requiresAdmin}`. ClearedAt=null nghĩa là còn cách ly, dù UnlockAfter đã qua. Chỉ reset an toàn sau tối thiểu5 phút và telemetry dưới35°C mới xóa cách ly. Snapshot thiết bị có trường cooldowns; SlotTelemetry thêm isOverheated=false tùy chọn. Chưa hết5 phút trả409 cho yêu cầu mở/reset/thay pin không hợp lệ.

Cổng broker vẫn mặc định18883/19001. Cấu hình Broker:Port, Broker:WebSocketUrl, Broker:ClientId chỉ được tách riêng trong bộ kiểm thử cô lập.

## Reset mô phỏng và phục hồi nhiệt

POST /api/v1/stations/{station}/simulation/reset (Admin): `{requestId,clientId,leaseId}` → `{resetId,stationId,leaseId}`. Chống lặp; trả409 nếu có giao dịch/bảo trì/lệnh đang dang dở hoặc lease không khớp. Cùng request sau mất phản hồi trả kết quả đã lưu; không random hai lần.

Cooldowns bổ sung incidentCount/requiresAdmin. Các đợt1–2 tự phục hồi bằng AUTO_RESET_ALARM sau5 phút và nhiệt độ dưới35°C. Đợt4 chỉ Admin được POST maintenance với RESET_ALARM. Command thêm thermalStartedAt/adminThermalReset/preserveStationLock; các field do server phát, không dùng nội dung ACK để tự cấp quyền Admin.
