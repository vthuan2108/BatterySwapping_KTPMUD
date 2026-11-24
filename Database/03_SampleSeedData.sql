-- ==============================================================================
-- HỆ THỐNG QUẢN LÝ MẠNG LƯỚI TỦ ĐỔI PIN TỰ ĐỘNG CHO XE MÁY ĐIỆN
-- TẬP LỆNH: DỮ LIỆU KHỞI TẠO MẪU (SEED DATA)
-- ==============================================================================

USE [BatterySwappingDB];
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. XÓA DỮ LIỆU CŨ THEO THỨ TỰ RÀNG BUỘC
DELETE FROM dbo.CanhBaoIoT;
DELETE FROM dbo.LichSuDoiPin;
DELETE FROM dbo.LichSuNapTien;
DELETE FROM dbo.NganTu;
DELETE FROM dbo.Pin;
DELETE FROM dbo.KhachHang;
DELETE FROM dbo.TramPin;
DELETE FROM dbo.TaiKhoan;
GO

-- Reset identity seeds
DBCC CHECKIDENT ('dbo.TaiKhoan', RESEED, 0);
DBCC CHECKIDENT ('dbo.KhachHang', RESEED, 0);
DBCC CHECKIDENT ('dbo.NganTu', RESEED, 0);
DBCC CHECKIDENT ('dbo.LichSuDoiPin', RESEED, 0);
DBCC CHECKIDENT ('dbo.LichSuNapTien', RESEED, 0);
DBCC CHECKIDENT ('dbo.CanhBaoIoT', RESEED, 0);
GO

-- 2. TẠO TÀI KHOẢN (Mật khẩu mặc định: '123456')
-- Ghi chú: Mã băm SHA-256 của '123456' là: 8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92
SET IDENTITY_INSERT dbo.TaiKhoan ON;
INSERT INTO dbo.TaiKhoan (MaTK, TenDangNhap, MatKhau, HoTen, VaiTro, TrangThai) VALUES
(1, 'admin', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', N'Quản trị viên Trung tâm', N'Admin', N'HoatDong'),
(2, 'tech01', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', N'Kỹ thuật viên Trạm Bách Khoa', N'KyThuatVien', N'HoatDong'),
(3, 'driver01', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', N'Nguyễn Văn An', N'KhachHang', N'HoatDong'),
(4, 'driver02', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', N'Trần Thị Bình', N'KhachHang', N'HoatDong'),
(5, 'driver03', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', N'Lê Hoàng Cường', N'KhachHang', N'HoatDong'),
(6, 'driver04', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', N'Phạm Văn Dũng', N'KhachHang', N'HoatDong');
SET IDENTITY_INSERT dbo.TaiKhoan OFF;
GO

-- 3. TẠO THÔNG TIN TRẠM ĐỔI PIN
INSERT INTO dbo.TramPin (MaTram, TenTram, DiaChi, ToaDoGPS, SoLuongNgan, TrangThaiHoatDong) VALUES
('TRAM-BK01', N'Trạm Đổi Pin ĐH Bách Khoa Hà Nội', N'Cổng Parabol Giải Phóng, P. Bách Khoa, Q. Hai Bà Trưng, Hà Nội', '21.0048, 105.8432', 8, N'HoatDong'),
('TRAM-CG02', N'Trạm Đổi Pin Cầu Giấy', N'Số 144 Xuân Thủy, Q. Cầu Giấy, Hà Nội', '21.0366, 105.7816', 8, N'HoatDong');
GO

-- 4. TẠO DANH SÁCH PIN (PIN-001 -> PIN-016)
INSERT INTO dbo.Pin (MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai) VALUES
('PIN-001', N'Lithium LFP 72V-30Ah', 30, 98.50, 99.00, 27.50, 72.80, 0.00, 45, N'DaDay'),
('PIN-002', N'Lithium LFP 72V-30Ah', 30, 95.00, 97.50, 28.20, 72.50, 0.00, 62, N'DaDay'),
('PIN-003', N'Lithium LFP 72V-30Ah', 30, 92.00, 96.00, 30.10, 72.20, 0.00, 78, N'DaDay'),
('PIN-004', N'Lithium LFP 72V-30Ah', 30, 65.00, 95.00, 35.40, 71.00, 15.00, 89, N'DangSac'),
('PIN-005', N'Lithium LFP 72V-30Ah', 30, 42.00, 94.00, 37.80, 70.20, 15.00, 105, N'DangSac'),
('PIN-006', N'Lithium LFP 72V-30Ah', 30, 96.00, 97.00, 28.00, 72.60, 0.00, 50, N'DaDay'),
('PIN-007', N'Lithium LFP 72V-30Ah', 30, 78.00, 90.00, 34.50, 71.40, 12.00, 135, N'DangSac'),
('PIN-008', N'Lithium LFP 72V-30Ah', 30, 91.00, 95.50, 29.00, 72.10, 0.00, 52, N'DaDay'),
('PIN-009', N'Lithium LFP 72V-30Ah', 30, 18.00, 95.00, 31.50, 68.50, -10.00, 91, N'DangSuDung'), -- Khách Nguyễn Văn An đang gắn trên xe
('PIN-010', N'Lithium LFP 72V-30Ah', 30, 12.00, 93.00, 32.20, 67.80, -8.00, 114, N'DangSuDung'),  -- Khách Trần Thị Bình đang gắn trên xe
('PIN-011', N'Lithium LFP 72V-30Ah', 30, 55.00, 92.00, 33.00, 70.50, 15.00, 110, N'DangSac'),
('PIN-012', N'Lithium LFP 72V-30Ah', 30, 100.0, 98.00, 26.00, 73.00, 0.00, 30, N'DaDay'),
('PIN-013', N'Lithium LFP 72V-30Ah', 30, 94.00, 96.50, 27.80, 72.40, 0.00, 40, N'DaDay'),
('PIN-014', N'Lithium LFP 72V-30Ah', 30, 35.00, 91.00, 36.20, 69.80, 15.00, 125, N'DangSac');
GO

-- 5. TẠO KHÁCH HÀNG
SET IDENTITY_INSERT dbo.KhachHang ON;
INSERT INTO dbo.KhachHang (MaKH, MaTK, HoTen, SoDienThoai, Email, BienSoXe, SoDuVi, MaPinDangMuon) VALUES
(1, 3, N'Nguyễn Văn An', '0987654321', 'an.nv@gmail.com', '29A1-12345', 150000.00, 'PIN-009'),
(2, 4, N'Trần Thị Bình', '0912345678', 'binh.tt@gmail.com', '29B1-67890', 50000.00, 'PIN-010'),
(3, 5, N'Lê Hoàng Cường', '0905112233', 'cuong.lh@gmail.com', '29C1-99999', 10000.00, NULL), -- Số dư 10k < 25k (dùng để test lỗi số dư không đủ)
(4, 6, N'Phạm Văn Dũng', '0901234567', 'dung.pv@gmail.com', '29D1-55555', 120000.00, NULL);
SET IDENTITY_INSERT dbo.KhachHang OFF;
GO

-- 6. TẠO 8 NGĂN TỦ CHO TRẠM TRAM-BK01 (Trạm ĐH Bách Khoa)
INSERT INTO dbo.NganTu (MaTram, SoThuTu, CuaDangMo, KhoaBaoTri, MaPinHienTai, TrangThaiNgan) VALUES
('TRAM-BK01', 1, 0, 0, 'PIN-001', N'CoPin'),     -- Ngăn 1: Pin 98.5% (Sẵn sàng cấp)
('TRAM-BK01', 2, 0, 0, 'PIN-002', N'CoPin'),     -- Ngăn 2: Pin 95.0% (Sẵn sàng cấp)
('TRAM-BK01', 3, 0, 0, 'PIN-003', N'CoPin'),     -- Ngăn 3: Pin 92.0% (Sẵn sàng cấp)
('TRAM-BK01', 4, 0, 0, 'PIN-004', N'DangSac'),   -- Ngăn 4: Pin 65.0% (Đang sạc)
('TRAM-BK01', 5, 0, 0, 'PIN-005', N'DangSac'),   -- Ngăn 5: Pin 42.0% (Đang sạc)
('TRAM-BK01', 6, 0, 0, NULL,      N'Trong'),     -- Ngăn 6: TRỐNG (Mở nhận pin cạn)
('TRAM-BK01', 7, 0, 0, NULL,      N'Trong'),     -- Ngăn 7: TRỐNG (Mở nhận pin cạn)
('TRAM-BK01', 8, 0, 0, 'PIN-008', N'CoPin');     -- Ngăn 8: Pin 91.0% (Sẵn sàng cấp)
GO

-- 7. TẠO 8 NGĂN TỦ CHO TRẠM TRAM-CG02 (Trạm Cầu Giấy)
INSERT INTO dbo.NganTu (MaTram, SoThuTu, CuaDangMo, KhoaBaoTri, MaPinHienTai, TrangThaiNgan) VALUES
('TRAM-CG02', 1, 0, 0, 'PIN-006', N'CoPin'),     -- Ngăn 1: Pin 96.0% (Sẵn sàng cấp)
('TRAM-CG02', 2, 0, 0, 'PIN-007', N'DangSac'),   -- Ngăn 2: Pin 78.0% (Đang sạc)
('TRAM-CG02', 3, 0, 0, 'PIN-011', N'DangSac'),   -- Ngăn 3: Pin 55.0% (Đang sạc)
('TRAM-CG02', 4, 0, 0, 'PIN-012', N'CoPin'),     -- Ngăn 4: Pin 100.0% (Sẵn sàng cấp)
('TRAM-CG02', 5, 0, 0, 'PIN-013', N'CoPin'),     -- Ngăn 5: Pin 94.0% (Sẵn sàng cấp)
('TRAM-CG02', 6, 0, 0, 'PIN-014', N'DangSac'),   -- Ngăn 6: Pin 35.0% (Đang sạc)
('TRAM-CG02', 7, 0, 0, NULL,      N'Trong'),     -- Ngăn 7: TRỐNG (Nhận pin cạn)
('TRAM-CG02', 8, 0, 0, NULL,      N'Trong');     -- Ngăn 8: TRỐNG (Nhận pin cạn)
GO

-- 8. TẠO LỊCH SỬ NẠP TIỀN MẪU
INSERT INTO dbo.LichSuNapTien (MaKH, SoTien, PhuongThuc, MaGiaoDichNganHang, ThoiGian) VALUES
(1, 200000.00, N'ChuyenKhoan', 'MB-10293847', DATEADD(DAY, -2, GETDATE())),
(2, 100000.00, N'ChuyenKhoan', 'VCB-55443322', DATEADD(DAY, -1, GETDATE())),
(3, 50000.00,  N'TienMat',     'CASH-0001',   DATEADD(DAY, -3, GETDATE()));
GO

-- 9. TẠO LỊCH SỬ ĐỔI PIN MẪU
INSERT INTO dbo.LichSuDoiPin (MaKH, MaTram, MaPinTra, SoC_PinTra, NganTra, MaPinNhan, SoC_PinNhan, NganNhan, ThoiGianBatDau, ThoiGianKetThuc, PhiDoiPin, TrangThai, GhiChu) VALUES
(1, 'TRAM-BK01', 'PIN-011', 14.50, 6, 'PIN-009', 99.00, 1, DATEADD(MINUTE, -45, GETDATE()), DATEADD(MINUTE, -43, GETDATE()), 25000.00, N'HoanThanh', N'Đổi pin định kỳ thành công'),
(2, 'TRAM-BK01', 'PIN-012', 11.00, 7, 'PIN-010', 98.00, 2, DATEADD(MINUTE, -15, GETDATE()), DATEADD(MINUTE, -13, GETDATE()), 25000.00, N'HoanThanh', N'Đổi pin nhanh thành công');
GO

-- 10. TẠO CẢNH BÁO MẪU
INSERT INTO dbo.CanhBaoIoT (MaTram, MaNgan, MaPin, LoaiCanhBao, GiaTriDo, MucDoNghiemTrong, DaXuLy, GhiChu) VALUES
('TRAM-BK01', 5, 'PIN-005', N'Nhiệt độ ngăn sạc tăng nhanh', 42.50, N'CanhBao', 0, N'Nhiệt độ vượt 40°C trong khi sạc nhanh');
GO

PRINT N'>>> ĐÃ KHỞI TẠO DỮ LIỆU MẪU CHO CSDL BATTERYSWAPPINGDB THÀNH CÔNG! <<<';
