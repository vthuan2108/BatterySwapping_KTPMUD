-- ==============================================================================
-- HỆ THỐNG QUẢN LÝ MẠNG LƯỚI TỦ ĐỔI PIN TỰ ĐỘNG CHO XE MÁY ĐIỆN
-- TẬP LỆNH: TRIGGER TỰ ĐỘNG & STORED PROCEDURES (ACID COMPLIANT)
-- ==============================================================================

USE [BatterySwappingDB];
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. TRIGGER: TỰ ĐỘNG PHÁT HIỆN QUÁ NHIỆT PIN VÀ KÍCH HOẠT CẢNH BÁO IOT KHẨN CẤP

CREATE OR ALTER TRIGGER dbo.trg_KiemTraNhietDoPin
ON dbo.Pin
AFTER UPDATE, INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- Kiểm tra nếu nhiệt độ pin bị đẩy lên >= 55 độ C
    IF UPDATE(NhietDo) OR NOT EXISTS (SELECT 1 FROM deleted)
    BEGIN
        -- Chỉ ghi nhận cảnh báo nếu chưa có cảnh báo 'KhanCap' chưa xử lý cho pin này
        INSERT INTO dbo.CanhBaoIoT (MaTram, MaNgan, MaPin, LoaiCanhBao, GiaTriDo, MucDoNghiemTrong, DaXuLy, GhiChu)
        SELECT 
            ISNULL(nt.MaTram, 'TRAM-BK01'),
            nt.SoThuTu,
            i.MaPin,
            N'Sự cố quá nhiệt pin (> 55°C) - Tự động ngắt sạc khẩn cấp',
            i.NhietDo,
            N'KhanCap',
            0,
            N'Cảm biến IoT BMS phát hiện nhiệt độ đạt ' + CAST(i.NhietDo AS NVARCHAR(10)) + N'°C. Hệ thống đã kích hoạt khóa tủ an toàn.'
        FROM inserted i
        LEFT JOIN dbo.NganTu nt ON nt.MaPinHienTai = i.MaPin
        WHERE i.NhietDo >= 55.0 
          AND NOT EXISTS (
              SELECT 1 FROM dbo.CanhBaoIoT cb 
              WHERE cb.MaPin = i.MaPin AND cb.DaXuLy = 0 AND cb.MucDoNghiemTrong = N'KhanCap'
          );

        -- Tự động đánh dấu trạng thái Pin bị lỗi quá nhiệt và ngắt dòng sạc về 0A
        UPDATE p
        SET p.TrangThai = N'LoiQuaNhiet', p.DongDien = 0.0, p.NgayCapNhatCuoi = GETDATE()
        FROM dbo.Pin p
        INNER JOIN inserted i ON p.MaPin = i.MaPin
        WHERE i.NhietDo >= 55.0 AND (p.TrangThai <> N'LoiQuaNhiet' OR p.DongDien > 0.0);

        -- Khóa ngăn tủ tương ứng
        UPDATE nt
        SET nt.KhoaBaoTri = 1, nt.TrangThaiNgan = N'Loi', nt.NgayCapNhat = GETDATE()
        FROM dbo.NganTu nt
        INNER JOIN inserted i ON nt.MaPinHienTai = i.MaPin
        WHERE i.NhietDo >= 55.0 AND nt.KhoaBaoTri = 0;
    END
END;
GO

-- 2. STORED PROCEDURE: THỰC HIỆN CHU KỲ ĐỔI PIN AN TOÀN (TRANSACTION SCOPE - ACID)

CREATE OR ALTER PROCEDURE dbo.sp_ThucHienDoiPin
    @MaKH INT,
    @MaTram VARCHAR(20),
    @MaPinTra VARCHAR(20),
    @SoC_PinTra DECIMAL(5,2),
    @NganTra INT,
    @MaPinNhan VARCHAR(20),
    @SoC_PinNhan DECIMAL(5,2),
    @NganNhan INT,
    @PhiDoiPin DECIMAL(18,2) = 25000,
    @MaGD INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Khóa và kiểm tra số dư ví khách hàng (chống Race Condition và Double Spending)
        DECLARE @SoDuHienTai DECIMAL(18,2);
        SELECT @SoDuHienTai = SoDuVi 
        FROM dbo.KhachHang WITH (UPDLOCK, ROWLOCK) 
        WHERE MaKH = @MaKH;

        IF @SoDuHienTai IS NULL
        BEGIN
            RAISERROR(N'Không tìm thấy thông tin khách hàng trong hệ thống.', 16, 1);
        END

        IF @SoDuHienTai < @PhiDoiPin
        BEGIN
            RAISERROR(N'Số dư ví không đủ để thực hiện đổi pin (yêu cầu tối thiểu 25.000 VNĐ).', 16, 1);
        END

        -- 2. Kiểm tra ngăn trả và ngăn nhận có tồn tại và không bị khóa bảo trì
        IF NOT EXISTS (SELECT 1 FROM dbo.NganTu WHERE MaTram = @MaTram AND SoThuTu = @NganTra AND KhoaBaoTri = 0)
        BEGIN
            RAISERROR(N'Ngăn nhận pin cũ không khả dụng hoặc đang bị khóa bảo trì.', 16, 1);
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.NganTu WHERE MaTram = @MaTram AND SoThuTu = @NganNhan AND KhoaBaoTri = 0)
        BEGIN
            RAISERROR(N'Ngăn cấp pin mới không khả dụng hoặc đang bị khóa bảo trì.', 16, 1);
        END

        -- Serialize station swaps and revalidate the actual inventory at commit time.
        DECLARE @LockResult INT;
        EXEC @LockResult = sys.sp_getapplock @Resource = @MaTram,
             @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
        IF @LockResult < 0 THROW 50001, N'Tram dang ban. Vui long thu lai.', 1;
        IF @PhiDoiPin <= 0 OR @NganTra = @NganNhan OR @MaPinTra = @MaPinNhan
            THROW 50002, N'Du lieu doi pin khong hop le.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.TramPin WITH (UPDLOCK, HOLDLOCK)
                       WHERE MaTram = @MaTram AND TrangThaiHoatDong = N'HoatDong')
            THROW 50003, N'Tram khong hoat dong.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.NganTu WITH (UPDLOCK, HOLDLOCK)
                       WHERE MaTram = @MaTram AND SoThuTu = @NganTra
                         AND KhoaBaoTri = 0 AND MaPinHienTai IS NULL AND TrangThaiNgan <> N'Loi')
            THROW 50004, N'Ngan tra khong con trong.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.NganTu nt WITH (UPDLOCK, HOLDLOCK)
                       JOIN dbo.Pin p WITH (UPDLOCK, HOLDLOCK) ON p.MaPin = nt.MaPinHienTai
                       WHERE nt.MaTram = @MaTram AND nt.SoThuTu = @NganNhan
                         AND nt.KhoaBaoTri = 0 AND nt.TrangThaiNgan <> N'Loi'
                         AND p.MaPin = @MaPinNhan AND p.SoC >= 90 AND p.SoH >= 80
                         AND p.NhietDo <= 45 AND p.TrangThai IN (N'DaDay', N'DangSac', N'KhaDung'))
            THROW 50005, N'Pin nhan khong con kha dung.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.KhachHang
                       WHERE MaKH = @MaKH AND MaPinDangMuon = @MaPinTra)
            THROW 50006, N'Pin tra khong thuoc khach hang.', 1;
        IF EXISTS (SELECT 1 FROM dbo.NganTu WITH (UPDLOCK, HOLDLOCK) WHERE MaPinHienTai = @MaPinTra)
            THROW 50007, N'Pin tra dang o trong tu.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Pin WITH (UPDLOCK, HOLDLOCK)
                       WHERE MaPin = @MaPinTra AND NhietDo < 55 AND TrangThai <> N'LoiQuaNhiet')
            THROW 50008, N'Pin tra khong an toan hoac khong ton tai.', 1;

        -- 3. Trừ tiền ví khách hàng và cập nhật mã pin mới đang mượn
        UPDATE dbo.KhachHang
        SET SoDuVi = SoDuVi - @PhiDoiPin,
            MaPinDangMuon = @MaPinNhan
        WHERE MaKH = @MaKH;

        -- 4. Đảm bảo mã pin trả đã được giải phóng khỏi ngăn cũ nếu trước đó có dữ liệu xung đột (chống lỗi UQ_NganTu_MaPinHienTai)
        UPDATE dbo.NganTu
        SET MaPinHienTai = NULL, TrangThaiNgan = N'Trong', NgayCapNhat = GETDATE()
        WHERE MaPinHienTai = @MaPinTra AND NOT (MaTram = @MaTram AND SoThuTu = @NganTra);

        -- Đảm bảo mã pin trả tồn tại trong bảng Pin (chống lỗi Foreign Key)
        IF NOT EXISTS (SELECT 1 FROM dbo.Pin WHERE MaPin = @MaPinTra)
        BEGIN
            INSERT INTO dbo.Pin (MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi)
            VALUES (@MaPinTra, N'Lithium LFP 72V-30Ah', 30, @SoC_PinTra, 95.00, 28.00, 72.00, 15.00, 1, N'DangSac', GETDATE());
        END
        ELSE
        BEGIN
            UPDATE dbo.Pin
            SET SoC = @SoC_PinTra,
                TrangThai = N'DangSac',
                DongDien = 15.0, -- Bắt đầu nạp dòng 15A
                NgayCapNhatCuoi = GETDATE()
            WHERE MaPin = @MaPinTra;
        END

        -- Cập nhật pin trả vào ngăn tủ trả (Bắt đầu sạc, đóng cửa)
        UPDATE dbo.NganTu
        SET MaPinHienTai = @MaPinTra,
            TrangThaiNgan = N'DangSac',
            CuaDangMo = 0,
            NgayCapNhat = GETDATE()
        WHERE MaTram = @MaTram AND SoThuTu = @NganTra;

        -- 5. Nhả pin mới ra khỏi ngăn tủ nhận (Ngăn nhận trở thành Trống)
        UPDATE dbo.Pin
        SET TrangThai = N'DangSuDung',
            DongDien = 0.0,
            SoLanSac = SoLanSac + 1,
            NgayCapNhatCuoi = GETDATE()
        WHERE MaPin = @MaPinNhan;

        UPDATE dbo.NganTu
        SET MaPinHienTai = NULL,
            TrangThaiNgan = N'Trong',
            CuaDangMo = 0,
            NgayCapNhat = GETDATE()
        WHERE MaTram = @MaTram AND SoThuTu = @NganNhan;

        -- 6. Ghi lịch sử giao dịch đổi pin
        INSERT INTO dbo.LichSuDoiPin (
            MaKH, MaTram, MaPinTra, SoC_PinTra, NganTra, 
            MaPinNhan, SoC_PinNhan, NganNhan, 
            ThoiGianBatDau, ThoiGianKetThuc, PhiDoiPin, TrangThai, GhiChu
        )
        VALUES (
            @MaKH, @MaTram, @MaPinTra, @SoC_PinTra, @NganTra,
            @MaPinNhan, @SoC_PinNhan, @NganNhan,
            DATEADD(MINUTE, -2, GETDATE()), GETDATE(), @PhiDoiPin, N'HoanThanh', N'Giao dịch đổi pin tự động thành công'
        );

        SET @MaGD = SCOPE_IDENTITY();

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- 3. STORED PROCEDURE: NẠP TIỀN VÀO VÍ KHÁCH HÀNG (TRANSACTION SCOPE)

CREATE OR ALTER PROCEDURE dbo.sp_NapTienVi
    @MaKH INT,
    @SoTien DECIMAL(18,2),
    @PhuongThuc NVARCHAR(50) = N'ChuyenKhoan',
    @MaGiaoDichNganHang VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @SoTien <= 0
        BEGIN
            RAISERROR(N'Số tiền nạp phải lớn hơn 0 VNĐ.', 16, 1);
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.KhachHang WHERE MaKH = @MaKH)
        BEGIN
            RAISERROR(N'Khách hàng không tồn tại trong hệ thống.', 16, 1);
        END

        UPDATE dbo.KhachHang
        SET SoDuVi = SoDuVi + @SoTien
        WHERE MaKH = @MaKH;

        INSERT INTO dbo.LichSuNapTien (MaKH, SoTien, PhuongThuc, MaGiaoDichNganHang, ThoiGian, TrangThai)
        VALUES (@MaKH, @SoTien, @PhuongThuc, @MaGiaoDichNganHang, GETDATE(), N'ThanhCong');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- 4. STORED PROCEDURE: LẤY CHI TIẾT TRẠNG THÁI TRẠM SẠC & 8 NGĂN TỦ

CREATE OR ALTER PROCEDURE dbo.sp_LayChiTietTramVaNgan
    @MaTram VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        nt.MaNgan,
        nt.MaTram,
        nt.SoThuTu,
        nt.CuaDangMo,
        nt.KhoaBaoTri,
        nt.TrangThaiNgan,
        nt.MaPinHienTai,
        p.LoaiPin,
        ISNULL(p.SoC, 0.0) AS SoC,
        ISNULL(p.SoH, 0.0) AS SoH,
        ISNULL(p.NhietDo, 25.0) AS NhietDo,
        ISNULL(p.DienAp, 0.0) AS DienAp,
        ISNULL(p.DongDien, 0.0) AS DongDien,
        ISNULL(p.SoLanSac, 0) AS SoLanSac,
        ISNULL(p.TrangThai, N'Trong') AS TrangThaiPin
    FROM dbo.NganTu nt
    LEFT JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin
    WHERE nt.MaTram = @MaTram
    ORDER BY nt.SoThuTu ASC;
END;
GO

-- 5. STORED PROCEDURE: KỸ THUẬT VIÊN BẢO TRÌ VÀ THAY THẾ PIN (TRANSACTION SCOPE - ACID)

CREATE OR ALTER PROCEDURE dbo.sp_BaoTriThayThePin
    @MaTram VARCHAR(20),
    @SoThuTu INT,
    @MaPinCu VARCHAR(20),
    @MaPinMoi VARCHAR(20) = NULL,
    @NguoiThucHien NVARCHAR(100),
    @GhiChu NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Kiểm tra ngăn tủ tại trạm
        IF NOT EXISTS (SELECT 1 FROM dbo.NganTu WHERE MaTram = @MaTram AND SoThuTu = @SoThuTu)
        BEGIN
            RAISERROR(N'Ngăn tủ không tồn tại tại trạm chỉ định.', 16, 1);
        END

        -- 2. Trường hợp thay thế pin chai/hỏng bằng pin mới
        IF @MaPinMoi IS NOT NULL AND LEN(LTRIM(RTRIM(@MaPinMoi))) > 0
        BEGIN
            -- Đánh dấu pin cũ đã thu hồi / ngừng sử dụng
            UPDATE dbo.Pin
            SET TrangThai = N'DaThuHoi',
                DongDien = 0.0,
                NgayCapNhatCuoi = GETDATE()
            WHERE MaPin = @MaPinCu;

            -- Nếu pin mới chưa có trong hệ thống, thêm mới pin đạt chuẩn 100% SoC & SoH
            IF NOT EXISTS (SELECT 1 FROM dbo.Pin WHERE MaPin = @MaPinMoi)
            BEGIN
                INSERT INTO dbo.Pin (MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi)
                VALUES (@MaPinMoi, N'Lithium LFP 72V-30Ah', 30, 100.00, 100.00, 25.00, 73.00, 0.00, 0, N'DaDay', GETDATE());
            END
            ELSE
            BEGIN
                UPDATE dbo.Pin
                SET TrangThai = N'DaDay',
                    NhietDo = 25.00,
                    SoC = 100.00,
                    DongDien = 0.00,
                    NgayCapNhatCuoi = GETDATE()
                WHERE MaPin = @MaPinMoi;
            END

            -- Đảm bảo pin mới không bị gán trùng ở ngăn khác
            UPDATE dbo.NganTu
            SET MaPinHienTai = NULL, TrangThaiNgan = N'Trong', NgayCapNhat = GETDATE()
            WHERE MaPinHienTai = @MaPinMoi AND NOT (MaTram = @MaTram AND SoThuTu = @SoThuTu);

            -- Gán pin mới vào ngăn tủ và mở khóa hoạt động bình thường
            UPDATE dbo.NganTu
            SET MaPinHienTai = @MaPinMoi,
                TrangThaiNgan = N'CoPin',
                KhoaBaoTri = 0,
                CuaDangMo = 0,
                NgayCapNhat = GETDATE()
            WHERE MaTram = @MaTram AND SoThuTu = @SoThuTu;
        END
        ELSE
        BEGIN
            -- 3. Trường hợp bảo dưỡng/làm mát và hoàn tất sự cố tại chỗ (không thay pin)
            IF @MaPinCu IS NOT NULL AND LEN(LTRIM(RTRIM(@MaPinCu))) > 0
            BEGIN
                UPDATE dbo.Pin
                SET TrangThai = CASE WHEN SoC >= 90.0 THEN N'DaDay' ELSE N'DangSac' END,
                    NhietDo = 25.00,
                    DongDien = CASE WHEN SoC >= 100.0 THEN 0.0 ELSE 15.0 END,
                    NgayCapNhatCuoi = GETDATE()
                WHERE MaPin = @MaPinCu;

                UPDATE dbo.NganTu
                SET KhoaBaoTri = 0,
                    TrangThaiNgan = CASE WHEN SoC >= 90.0 THEN N'CoPin' ELSE N'DangSac' END,
                    NgayCapNhat = GETDATE()
                FROM dbo.NganTu nt
                INNER JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin
                WHERE nt.MaTram = @MaTram AND nt.SoThuTu = @SoThuTu;
            END
        END

        -- 4. Tự động giải quyết các cảnh báo an toàn tồn đọng của pin cũ
        IF @MaPinCu IS NOT NULL
        BEGIN
            UPDATE dbo.CanhBaoIoT
            SET DaXuLy = 1,
                NguoiXuLy = @NguoiThucHien,
                ThoiGianXuLy = GETDATE(),
                GhiChu = ISNULL(@GhiChu, N'Kỹ thuật viên đã xử lý bảo trì/thay thế pin thành công')
            WHERE MaPin = @MaPinCu AND DaXuLy = 0;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- 6. STORED PROCEDURE: XỬ LÝ VÀ ĐÓNG CẢNH BÁO AN TOÀN IOT KÈM MỞ KHÓA NGĂN TỦ

CREATE OR ALTER PROCEDURE dbo.sp_GiaiQuyetCanhBaoAnToan
    @MaCB INT,
    @NguoiXuLy NVARCHAR(100),
    @GhiChu NVARCHAR(255) = NULL,
    @MoKhoaNganTu BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @MaTram VARCHAR(20), @MaNgan INT, @MaPin VARCHAR(20);

        SELECT @MaTram = MaTram, @MaNgan = MaNgan, @MaPin = MaPin
        FROM dbo.CanhBaoIoT
        WHERE MaCB = @MaCB;

        IF @MaTram IS NULL
        BEGIN
            RAISERROR(N'Không tìm thấy bản ghi cảnh báo sự cố.', 16, 1);
        END

        -- Cập nhật trạng thái đã xử lý cảnh báo
        UPDATE dbo.CanhBaoIoT
        SET DaXuLy = 1,
            NguoiXuLy = @NguoiXuLy,
            ThoiGianXuLy = GETDATE(),
            GhiChu = @GhiChu
        WHERE MaCB = @MaCB;

        -- Mở khóa ngăn tủ và khôi phục pin nếu được yêu cầu
        IF @MoKhoaNganTu = 1 AND @MaNgan IS NOT NULL
        BEGIN
            -- Khôi phục trạng thái pin về an toàn
            IF @MaPin IS NOT NULL
            BEGIN
                UPDATE dbo.Pin
                SET NhietDo = 26.00,
                    TrangThai = CASE WHEN SoC >= 90.0 THEN N'DaDay' ELSE N'DangSac' END,
                    DongDien = CASE WHEN SoC >= 100.0 THEN 0.0 ELSE 15.0 END,
                    NgayCapNhatCuoi = GETDATE()
                WHERE MaPin = @MaPin AND TrangThai = N'LoiQuaNhiet';
            END

            -- Mở khóa bảo trì ngăn tủ
            UPDATE dbo.NganTu
            SET KhoaBaoTri = 0,
                TrangThaiNgan = CASE WHEN MaPinHienTai IS NULL THEN N'Trong' ELSE N'CoPin' END,
                NgayCapNhat = GETDATE()
            WHERE MaTram = @MaTram AND SoThuTu = @MaNgan;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

