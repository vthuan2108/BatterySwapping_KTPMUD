SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.KhachHang', 'MaTheRFID') IS NULL
    ALTER TABLE dbo.KhachHang ADD MaTheRFID varchar(64) NULL;

IF COL_LENGTH('dbo.KhachHang', 'MaQR') IS NULL
    ALTER TABLE dbo.KhachHang ADD MaQR varchar(128) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_KhachHang_MaTheRFID' AND object_id = OBJECT_ID('dbo.KhachHang'))
    EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_KhachHang_MaTheRFID ON dbo.KhachHang(MaTheRFID) WHERE MaTheRFID IS NOT NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_KhachHang_MaQR' AND object_id = OBJECT_ID('dbo.KhachHang'))
    EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_KhachHang_MaQR ON dbo.KhachHang(MaQR) WHERE MaQR IS NOT NULL;');

EXEC(N'
UPDATE k
SET
    k.MaTheRFID = COALESCE(k.MaTheRFID, CASE t.TenDangNhap
        WHEN ''driver01'' THEN ''RFID-AN-8801''
        WHEN ''driver02'' THEN ''RFID-BINH-8802''
        WHEN ''driver03'' THEN ''RFID-CUONG-8803''
        WHEN ''driver04'' THEN ''RFID-DUNG-8804''
    END),
    k.MaQR = COALESCE(k.MaQR, CASE t.TenDangNhap
        WHEN ''driver01'' THEN ''BSS-QR-DRV01-AN''
        WHEN ''driver02'' THEN ''BSS-QR-DRV02-BINH''
        WHEN ''driver03'' THEN ''BSS-QR-DRV03-CUONG''
        WHEN ''driver04'' THEN ''BSS-QR-DRV04-DUNG''
    END)
FROM dbo.KhachHang k
JOIN dbo.TaiKhoan t ON t.MaTK = k.MaTK
WHERE t.TenDangNhap IN (''driver01'', ''driver02'', ''driver03'', ''driver04'')
  AND (k.MaTheRFID IS NULL OR k.MaQR IS NULL);
');

COMMIT;
