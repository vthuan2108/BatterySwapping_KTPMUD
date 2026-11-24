-- Existing procedures are maintained only in 02_TriggersAndProcedures.sql.
USE [BatterySwappingDB];
GO
-- 5. PROCEDURE: THÊM MỚI TRẠM ĐỔI PIN VÀ TỰ ĐỘNG KHỞI TẠO CÁC NGĂN TỦ

CREATE OR ALTER PROCEDURE dbo.sp_ThemTramDoiPinMoi
    @MaTram VARCHAR(20),
    @TenTram NVARCHAR(100),
    @DiaChi NVARCHAR(255),
    @ToaDoGPS VARCHAR(50) = NULL,
    @SoLuongNgan INT = 8
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.TramPin WHERE MaTram = @MaTram)
    BEGIN
        RAISERROR(N'Mã trạm đã tồn tại trong hệ thống.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Thêm trạm mới
        INSERT INTO dbo.TramPin (MaTram, TenTram, DiaChi, ToaDoGPS, SoLuongNgan, TrangThaiHoatDong, NgayLapDat)
        VALUES (@MaTram, @TenTram, @DiaChi, @ToaDoGPS, @SoLuongNgan, N'HoatDong', GETDATE());

        -- 2. Tự động sinh các ngăn tủ tương ứng
        DECLARE @i INT = 1;
        WHILE @i <= @SoLuongNgan
        BEGIN
            INSERT INTO dbo.NganTu (MaTram, SoThuTu, CuaDangMo, KhoaBaoTri, TrangThaiNgan, MaPinHienTai, NgayCapNhat)
            VALUES (@MaTram, @i, 0, 0, N'Trong', NULL, GETDATE());
            SET @i = @i + 1;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @Err NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@Err, 16, 1);
    END CATCH
END;
GO

PRINT '>>> [OK] ĐÃ KHỞI TẠO THÀNH CÔNG 5 STORED PROCEDURES HỆ THỐNG TRONG BATTERYSWAPPINGDB! <<<';
