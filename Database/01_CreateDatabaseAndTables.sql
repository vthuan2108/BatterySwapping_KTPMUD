-- ==============================================================================
-- HỆ THỐNG QUẢN LÝ MẠNG LƯỚI TỦ ĐỔI PIN TỰ ĐỘNG CHO XE MÁY ĐIỆN
-- TẬP LỆNH DDL: TẠO CƠ SỞ DỮ LIỆU VÀ 8 BẢNG QUAN HỆ (SQL SERVER)
-- ==============================================================================

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'BatterySwappingDB')
BEGIN
    CREATE DATABASE [BatterySwappingDB];
END
GO

USE [BatterySwappingDB];
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- DROP CÁC BẢNG THEO THỨ TỰ NGƯỢC RÀNG BUỘC KHÓA NGOẠI
IF OBJECT_ID('dbo.CanhBaoIoT', 'U') IS NOT NULL DROP TABLE dbo.CanhBaoIoT;
IF OBJECT_ID('dbo.LichSuDoiPin', 'U') IS NOT NULL DROP TABLE dbo.LichSuDoiPin;
IF OBJECT_ID('dbo.LichSuNapTien', 'U') IS NOT NULL DROP TABLE dbo.LichSuNapTien;
IF OBJECT_ID('dbo.NganTu', 'U') IS NOT NULL DROP TABLE dbo.NganTu;
IF OBJECT_ID('dbo.KhachHang', 'U') IS NOT NULL DROP TABLE dbo.KhachHang;
IF OBJECT_ID('dbo.Pin', 'U') IS NOT NULL DROP TABLE dbo.Pin;
IF OBJECT_ID('dbo.TramPin', 'U') IS NOT NULL DROP TABLE dbo.TramPin;
IF OBJECT_ID('dbo.TaiKhoan', 'U') IS NOT NULL DROP TABLE dbo.TaiKhoan;
GO

-- 1. BẢNG TÀI KHOẢN (TaiKhoan)
CREATE TABLE dbo.TaiKhoan (
    MaTK INT IDENTITY(1,1) PRIMARY KEY,
    TenDangNhap VARCHAR(50) NOT NULL UNIQUE,
    MatKhau VARCHAR(256) NOT NULL, -- Lưu chuỗi băm SHA-256 hoặc plain text hỗ trợ demo
    HoTen NVARCHAR(100) NOT NULL,
    VaiTro NVARCHAR(30) NOT NULL, -- 'Admin', 'KyThuatVien', 'KhachHang'
    TrangThai NVARCHAR(30) NOT NULL DEFAULT N'HoatDong', -- 'HoatDong', 'BiKhoa'
    NgayTao DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- 2. BẢNG KHÁCH HÀNG (KhachHang)
CREATE TABLE dbo.KhachHang (
    MaKH INT IDENTITY(1,1) PRIMARY KEY,
    MaTK INT NULL,
    HoTen NVARCHAR(100) NOT NULL,
    SoDienThoai VARCHAR(20) NOT NULL UNIQUE,
    Email VARCHAR(100) NULL,
    BienSoXe VARCHAR(20) NOT NULL,
    SoDuVi DECIMAL(18, 2) NOT NULL DEFAULT 0.00 CHECK (SoDuVi >= 0),
    MaPinDangMuon VARCHAR(20) NULL,
    NgayDangKy DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_KhachHang_TaiKhoan FOREIGN KEY (MaTK) REFERENCES dbo.TaiKhoan(MaTK) ON DELETE SET NULL
);
CREATE UNIQUE NONCLUSTERED INDEX UQ_KhachHang_MaTK ON dbo.KhachHang(MaTK) WHERE MaTK IS NOT NULL;
GO

-- 3. BẢNG TRẠM ĐỔI PIN (TramPin)
CREATE TABLE dbo.TramPin (
    MaTram VARCHAR(20) PRIMARY KEY,
    TenTram NVARCHAR(100) NOT NULL,
    DiaChi NVARCHAR(255) NOT NULL,
    ToaDoGPS VARCHAR(50) NULL,
    SoLuongNgan INT NOT NULL DEFAULT 8,
    TrangThaiHoatDong NVARCHAR(30) NOT NULL DEFAULT N'HoatDong', -- 'HoatDong', 'BaoTri', 'TamDung'
    NgayLapDat DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- 4. BẢNG PIN (Pin)
CREATE TABLE dbo.Pin (
    MaPin VARCHAR(20) PRIMARY KEY,
    LoaiPin NVARCHAR(50) NOT NULL DEFAULT N'Lithium-ion LFP 72V-30Ah',
    DungLuongThietKe INT NOT NULL DEFAULT 30, -- Ah
    SoC DECIMAL(5,2) NOT NULL DEFAULT 100.00 CHECK (SoC >= 0 AND SoC <= 100), -- State of Charge (%)
    SoH DECIMAL(5,2) NOT NULL DEFAULT 100.00 CHECK (SoH >= 0 AND SoH <= 100), -- State of Health (%)
    NhietDo DECIMAL(5,2) NOT NULL DEFAULT 25.00, -- Độ C
    DienAp DECIMAL(5,2) NOT NULL DEFAULT 72.00, -- Volts
    DongDien DECIMAL(5,2) NOT NULL DEFAULT 0.00, -- Amperes (dương: sạc, âm: xả)
    SoLanSac INT NOT NULL DEFAULT 0,
    TrangThai NVARCHAR(30) NOT NULL DEFAULT N'DaDay', -- 'DaDay', 'DangSac', 'KhaDung', 'DangSuDung', 'Loi', 'LoiQuaNhiet'
    NgayCapNhatCuoi DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- 5. BẢNG NGĂN TỦ (NganTu)
CREATE TABLE dbo.NganTu (
    MaNgan INT IDENTITY(1,1) PRIMARY KEY,
    MaTram VARCHAR(20) NOT NULL,
    SoThuTu INT NOT NULL, -- Số ngăn 1..8
    CuaDangMo BIT NOT NULL DEFAULT 0, -- 0: Khóa/Đóng, 1: Mở
    KhoaBaoTri BIT NOT NULL DEFAULT 0, -- 1: Khóa an toàn không cho sử dụng
    MaPinHienTai VARCHAR(20) NULL, -- Mỗi ngăn chứa tối đa 1 pin
    TrangThaiNgan NVARCHAR(30) NOT NULL DEFAULT N'Trong', -- 'Trong', 'CoPin', 'DangSac', 'Loi'
    NgayCapNhat DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_NganTu_TramPin FOREIGN KEY (MaTram) REFERENCES dbo.TramPin(MaTram) ON DELETE CASCADE,
    CONSTRAINT FK_NganTu_Pin FOREIGN KEY (MaPinHienTai) REFERENCES dbo.Pin(MaPin) ON DELETE SET NULL,
    CONSTRAINT UQ_Tram_SoThuTu UNIQUE (MaTram, SoThuTu)
);
CREATE UNIQUE NONCLUSTERED INDEX UQ_NganTu_MaPinHienTai ON dbo.NganTu(MaPinHienTai) WHERE MaPinHienTai IS NOT NULL;
GO

-- 6. BẢNG LỊCH SỬ ĐỔI PIN (LichSuDoiPin)
CREATE TABLE dbo.LichSuDoiPin (
    MaGD INT IDENTITY(1,1) PRIMARY KEY,
    MaKH INT NOT NULL,
    MaTram VARCHAR(20) NOT NULL,
    MaPinTra VARCHAR(20) NOT NULL,
    SoC_PinTra DECIMAL(5,2) NOT NULL,
    NganTra INT NULL,
    MaPinNhan VARCHAR(20) NOT NULL,
    SoC_PinNhan DECIMAL(5,2) NOT NULL,
    NganNhan INT NULL,
    ThoiGianBatDau DATETIME NOT NULL DEFAULT GETDATE(),
    ThoiGianKetThuc DATETIME NULL,
    PhiDoiPin DECIMAL(18,2) NOT NULL DEFAULT 25000.00,
    TrangThai NVARCHAR(30) NOT NULL DEFAULT N'HoanThanh', -- 'DangXuLy', 'HoanThanh', 'ThatBai'
    GhiChu NVARCHAR(255) NULL,
    CONSTRAINT FK_LSDoiPin_KhachHang FOREIGN KEY (MaKH) REFERENCES dbo.KhachHang(MaKH),
    CONSTRAINT FK_LSDoiPin_TramPin FOREIGN KEY (MaTram) REFERENCES dbo.TramPin(MaTram)
);
GO

-- 7. BẢNG LỊCH SỬ NẠP TIỀN VÍ (LichSuNapTien)
CREATE TABLE dbo.LichSuNapTien (
    MaNap INT IDENTITY(1,1) PRIMARY KEY,
    MaKH INT NOT NULL,
    SoTien DECIMAL(18,2) NOT NULL CHECK (SoTien > 0),
    PhuongThuc NVARCHAR(50) NOT NULL DEFAULT N'ChuyenKhoan', -- 'ChuyenKhoan', 'TienMat', 'TheNganHang'
    MaGiaoDichNganHang VARCHAR(50) NULL,
    ThoiGian DATETIME NOT NULL DEFAULT GETDATE(),
    TrangThai NVARCHAR(30) NOT NULL DEFAULT N'ThanhCong',
    CONSTRAINT FK_LSNapTien_KhachHang FOREIGN KEY (MaKH) REFERENCES dbo.KhachHang(MaKH) ON DELETE CASCADE
);
GO

-- 8. BẢNG CẢNH BÁO IOT & SỰ CỐ (CanhBaoIoT)
CREATE TABLE dbo.CanhBaoIoT (
    MaCB INT IDENTITY(1,1) PRIMARY KEY,
    MaTram VARCHAR(20) NOT NULL,
    MaNgan INT NULL,
    MaPin VARCHAR(20) NULL,
    LoaiCanhBao NVARCHAR(100) NOT NULL, -- 'QuaNhiet', 'ChaiPin', 'KetCua', 'DienApBatThuong'
    GiaTriDo DECIMAL(5,2) NULL, -- Giá trị đo thực tế (ví dụ nhiệt độ 62.5°C)
    ThoiGianPhatHien DATETIME NOT NULL DEFAULT GETDATE(),
    MucDoNghiemTrong NVARCHAR(30) NOT NULL DEFAULT N'CanhBao', -- 'ThongTin', 'CanhBao', 'KhanCap'
    DaXuLy BIT NOT NULL DEFAULT 0,
    NguoiXuLy NVARCHAR(100) NULL,
    ThoiGianXuLy DATETIME NULL,
    GhiChu NVARCHAR(255) NULL,
    CONSTRAINT FK_CanhBao_TramPin FOREIGN KEY (MaTram) REFERENCES dbo.TramPin(MaTram) ON DELETE CASCADE
);
GO
