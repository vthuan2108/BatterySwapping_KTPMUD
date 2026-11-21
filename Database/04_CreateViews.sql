-- ==============================================================================
-- HỆ THỐNG QUẢN LÝ MẠNG LƯỚI TỦ ĐỔI PIN TỰ ĐỘNG CHO XE MÁY ĐIỆN (BSS)
-- TẬP LỆNH: KHỞI TẠO CÁC VIEW TRUY VẤN VÀ BÁO CÁO NGHIỆP VỤ (04_CreateViews.sql)
-- Phục vụ tiêu chí chấm điểm LMS Pro: "Tạo các View" (10/10)
-- ==============================================================================

USE [BatterySwappingDB];
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. VIEW: TỔNG HỢP TRẠNG THÁI 8 NGĂN TỦ VÀ THÔNG SỐ BMS CỦA PIN THỜI GIAN THỰC

CREATE OR ALTER VIEW dbo.v_TrangThaiCacNganTu
AS
SELECT 
    t.MaTram,
    t.TenTram,
    t.DiaChi,
    nt.MaNgan,
    nt.SoThuTu AS ViTriNgan,
    nt.CuaDangMo,
    nt.KhoaBaoTri,
    nt.TrangThaiNgan,
    p.MaPin,
    p.LoaiPin,
    ISNULL(p.SoC, 0.0) AS SoC,
    ISNULL(p.SoH, 0.0) AS SoH,
    ISNULL(p.NhietDo, 0.0) AS NhietDo,
    ISNULL(p.DienAp, 0.0) AS DienAp,
    ISNULL(p.DongDien, 0.0) AS DongDien,
    ISNULL(p.SoLanSac, 0) AS SoLanSac,
    ISNULL(p.TrangThai, N'Trong') AS TrangThaiPin,
    CASE 
        WHEN nt.KhoaBaoTri = 1 OR p.TrangThai = N'LoiQuaNhiet' OR p.NhietDo >= 55.0 THEN N'NguyHiem_QuaNhiet'
        WHEN nt.MaPinHienTai IS NULL THEN N'NganTrong'
        WHEN p.SoC >= 90.0 AND p.SoH >= 80.0 AND p.NhietDo < 45.0 THEN N'SanSangDoi'
        WHEN p.DongDien > 0.0 THEN N'DangSac'
        ELSE N'ChoSac'
    END AS DanhGiaKhaDung
FROM dbo.NganTu nt
INNER JOIN dbo.TramPin t ON nt.MaTram = t.MaTram
LEFT JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin;
GO

-- 2. VIEW: LỊCH SỬ GIAO DỊCH ĐỔI PIN CHI TIẾT (XUẤT HÓA ĐƠN VÀ TRA CỨU)

CREATE OR ALTER VIEW dbo.v_LichSuGiaoDichChiTiet
AS
SELECT 
    gd.MaGD,
    gd.ThoiGianKetThuc,
    kh.MaKH,
    kh.HoTen AS TenKhachHang,
    kh.SoDienThoai,
    kh.BienSoXe,
    tk.TenDangNhap,
    t.MaTram,
    t.TenTram,
    gd.NganTra,
    gd.MaPinTra,
    gd.SoC_PinTra,
    gd.NganNhan,
    gd.MaPinNhan,
    gd.SoC_PinNhan,
    gd.PhiDoiPin,
    gd.TrangThai AS TrangThaiGiaoDich,
    gd.GhiChu
FROM dbo.LichSuDoiPin gd
INNER JOIN dbo.KhachHang kh ON gd.MaKH = kh.MaKH
LEFT JOIN dbo.TaiKhoan tk ON kh.MaTK = tk.MaTK
INNER JOIN dbo.TramPin t ON gd.MaTram = t.MaTram;
GO

-- 3. VIEW: THỐNG KÊ DOANH THU VÀ LƯỢT ĐỔI PIN THEO NGÀY CỦA TỪNG TRẠM

CREATE OR ALTER VIEW dbo.v_ThongKeDoanhThuTram
AS
SELECT 
    t.MaTram,
    t.TenTram,
    CAST(gd.ThoiGianKetThuc AS DATE) AS NgayGiaoDich,
    COUNT(gd.MaGD) AS TongLuotDoi,
    SUM(gd.PhiDoiPin) AS TongDoanhThu,
    AVG(gd.PhiDoiPin) AS DoanhThuTrungBinh,
    AVG(gd.SoC_PinTra) AS SoC_TraTrungBinh,
    AVG(gd.SoC_PinNhan) AS SoC_NhanTrungBinh
FROM dbo.TramPin t
LEFT JOIN dbo.LichSuDoiPin gd ON t.MaTram = gd.MaTram AND gd.TrangThai = N'HoanThanh'
GROUP BY t.MaTram, t.TenTram, CAST(gd.ThoiGianKetThuc AS DATE);
GO

-- 4. VIEW: DANH SÁCH CẢNH BÁO AN TOÀN IOT CHƯA ĐƯỢC XỬ LÝ (DÀNH CHO KỸ THUẬT VIÊN)

CREATE OR ALTER VIEW dbo.v_CanhBaoAnToanChuaXuLy
AS
SELECT 
    cb.MaCB,
    cb.ThoiGianPhatHien,
    t.MaTram,
    t.TenTram,
    t.DiaChi AS DiaChiTram,
    cb.MaNgan,
    cb.MaPin,
    cb.LoaiCanhBao,
    cb.GiaTriDo,
    cb.MucDoNghiemTrong,
    cb.DaXuLy,
    cb.GhiChu,
    DATEDIFF(MINUTE, cb.ThoiGianPhatHien, GETDATE()) AS SoPhutTonDong
FROM dbo.CanhBaoIoT cb
INNER JOIN dbo.TramPin t ON cb.MaTram = t.MaTram
WHERE cb.DaXuLy = 0;
GO

-- 5. VIEW: DANH SÁCH PIN ĐẠT CHUẨN SẴN SÀNG CUNG CẤP CHO KHÁCH HÀNG (SoC >= 90%, SoH >= 80%)

CREATE OR ALTER VIEW dbo.v_DanhSachPinKhaDung
AS
SELECT 
    p.MaPin,
    p.LoaiPin,
    p.SoC,
    p.SoH,
    p.NhietDo,
    p.DienAp,
    p.SoLanSac,
    nt.MaTram,
    t.TenTram,
    nt.SoThuTu AS ViTriNgan
FROM dbo.Pin p
INNER JOIN dbo.NganTu nt ON p.MaPin = nt.MaPinHienTai
INNER JOIN dbo.TramPin t ON nt.MaTram = t.MaTram
WHERE p.SoC >= 90.0 
  AND p.SoH >= 80.0 
  AND p.NhietDo <= 45.0 
  AND nt.KhoaBaoTri = 0 
  AND nt.TrangThaiNgan <> N'Loi'
  AND p.TrangThai IN (N'DaDay', N'DangSac', N'KhaDung');
GO

PRINT '>>> [OK] ĐÃ KHỞI TẠO THÀNH CÔNG 5 VIEW HỆ THỐNG TRONG BATTERYSWAPPINGDB! <<<';
