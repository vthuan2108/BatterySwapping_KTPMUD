# Chỉnh lại mô phỏng: reset, sạc theo thời gian và tự phục hồi nhiệt

Yêu cầu đã chốt: có reset giả lập; BMS ban đầu ngẫu nhiên hợp lý; sạc tính theo thời gian; hai đợt quá nhiệt đầu tự phục hồi sau tối thiểu5 phút thực và dưới35°C, đợt thứ3 khóa chờ admin. Mỗi đợt là một lần mới sau phục hồi, không đếm từng telemetry.

Quyết định triển khai: đếm liên tiếp theo pin tại ngăn từ lần xác nhận admin/reset kịch bản gần nhất; đổi pin sang viên khác bắt đầu lại đếm. Không tự xóa đếm chỉ vì nhiệt độ tạm giảm. Bản tin offline muộn bắt đầu khoảng5 phút bảo thủ từ khi server quan sát.

Reset kịch bản chỉ admin, phải sở hữu lease và không có giao dịch/bảo trì/lệnh đang dang dở. Đổi BMS của pin trong tủ, không đổi quyền giữ tài sản/ví/lịch sử. Dữ liệu random gồm SoC,SoH,nhiệt,chu kỳ; điện áp và dòng suy ra từ SoC; dung lượng thiết kế lấy cấu hình pin. Seed/ID pin không random để giữ đồng bộ. Giữ trạng thái dừng trạm nếu có.

Các phần: backend phục hồi tự động và đếm lỗi; reset kịch bản chống lặp; Web3D dùng delta thời gian và dung lượng thiết kế; WPF/Avalonia hiển thị đếm lỗi/tự phục hồi/khóa admin. Kiểm thử cô lập40+ tình huống cũ, đồng hồ giả và3 chu kỳ, reset khi bận, bảo toàn ví/tài sản, sạc theo Ah.
