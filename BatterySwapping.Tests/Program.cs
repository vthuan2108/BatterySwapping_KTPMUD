using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
using BatterySwapping.Simulator;

namespace BatterySwapping.Tests
{
    class Program
    {
        static int passed = 0;
        static int failed = 0;

        static void Main(string[] args)
        {
            if (Array.IndexOf(args, "--unit") >= 0)
            {
                RunTest("Password hash cannot be used as password", () => {
                    var hash = PasswordHelper.HashPassword("123456");
                    Assert(!PasswordHelper.VerifyPassword(hash, hash), "Stored hash accepted as password");
                    Assert(PasswordHelper.VerifyPassword("123456", hash), "Valid password rejected");
                    Assert(PasswordHelper.VerifyPassword("legacy", "legacy"), "Legacy password rejected");
                });
                RunTest("Malformed QR is rejected", () => {
                    Assert(!QrCodeHelper.TryParseQrPayload("BSS:KH-abc:0912345678:0", out _, out _), "Invalid ID accepted");
                    Assert(!QrCodeHelper.TryParseQrPayload("BSS:KH-1:bad-phone:0", out _, out _), "Invalid phone accepted");
                });
                Environment.ExitCode = failed == 0 ? 0 : 1;
                return;
            }
            if (Array.IndexOf(args, "--integration") < 0)
            { Console.WriteLine("Use --unit or --integration (isolated database only)."); return; }
            DatabaseHelper.ConnectionString = @"Server=.\SQLEXPRESS;Database=BatterySwappingDebug_20261121;Trusted_Connection=True;TrustServerCertificate=True;Pooling=True;Min Pool Size=5;Max Pool Size=100;";
            Console.WriteLine("===============================================================");
            Console.WriteLine("   BATTERY SWAPPING SYSTEM (BSS) - AUTOMATED VERIFICATION SUITE");
            Console.WriteLine("===============================================================");
            Console.WriteLine($"Database: {DatabaseHelper.ConnectionString}");
            Console.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("---------------------------------------------------------------\n");

            RunTest("TC-01: PasswordHelper SHA-256 Hash Generation", Test_PasswordHelper_Hash);
            RunTest("TC-02: PasswordHelper SHA-256 Hex Format Validation (IsSha256)", Test_PasswordHelper_IsSha256);
            RunTest("TC-03: PasswordHelper VerifyPassword (Hash & Seed Fallback)", Test_PasswordHelper_Verify);
            RunTest("TC-04: DatabaseHelper Connection Pooling & Command Timeout", Test_DatabaseHelper_PoolingAndTimeout);
            RunTest("TC-05: Database Specification - All 8 Required Tables Exist", Test_Database_8Tables);
            RunTest("TC-06: Database Specification - Stored Procedures Exist", Test_Database_StoredProcedures);
            RunTest("TC-07: Database Specification - IoT Safety Trigger trg_KiemTraNhietDoPin", Test_Database_Trigger);
            RunTest("TC-08: AuthService Login - Admin Authentication & CurrentUser", Test_AuthService_AdminLogin);
            RunTest("TC-09: AuthService Login - KyThuatVien Technician Role & Session", Test_AuthService_TechLogin);
            RunTest("TC-10: AuthService Login - KhachHang Driver Role & Session", Test_AuthService_DriverLogin);
            RunTest("TC-11: AuthService Login - Invalid Password Rejection", Test_AuthService_InvalidPassword);
            RunTest("TC-12: AuthService Login - Non-existent Username Rejection", Test_AuthService_NonExistentUser);
            RunTest("TC-13: StationService - Retrieve All Stations (TRAM-BK01, TRAM-CG02)", Test_StationService_GetAllStations);
            RunTest("TC-14: StationService - Retrieve 8 Cabinet Slots for TRAM-BK01", Test_StationService_GetCabinetSlots);
            RunTest("TC-15: BatteryService - Retrieve All Batteries (14+ units)", Test_BatteryService_GetAllBatteries);
            RunTest("TC-16: BatteryService - Query Battery Details by ID (PIN-001)", Test_BatteryService_GetBatteryById);
            RunTest("TC-17: BatteryService - BMS Inspection & Health Diagnostics (KyThuatVien)", Test_BatteryService_InspectBattery);
            RunTest("TC-18: QrCodeHelper - Roundtrip Generation & Parsing", Test_QrCodeHelper_Roundtrip);
            RunTest("TC-19: SwapService - Customer Wallet Validation & Insufficient Balance Check", Test_SwapService_WalletValidation);
            RunTest("TC-20: SwapService - Empty Slot Detection for Old Battery Return", Test_SwapService_FindAvailableEmptySlot);
            RunTest("TC-21: SwapService - Optimal Battery Selection Algorithm (SoC >= 90%, SoH >= 80%)", Test_SwapService_FindOptimalBatterySlot);
            RunTest("TC-22: ReportService - Financial & Swapping Summary Statistics Aggregation", Test_ReportService_GetSummaryStats);
            RunTest("TC-23: SafetyMonitorService - Background IoT Telemetry Scanner", Test_SafetyMonitorService_Scan);
            RunTest("TC-24: SimulatorWorker - Automated Charging Simulation Engine", Test_SimulatorWorker_Lifecycle);
            RunTest("TC-25: WPF Thread Safety - Frozen Brushes Across Models", Test_Wpf_FrozenBrushes);

            RunTest("All SQL views execute", () => {
                foreach (var view in new[] { "v_TrangThaiCacNganTu", "v_LichSuGiaoDichChiTiet", "v_ThongKeDoanhThuTram", "v_CanhBaoAnToanChuaXuLy", "v_DanhSachPinKhaDung" })
                    DatabaseHelper.ExecuteDataTable("SELECT * FROM dbo." + view);
            });
            RunTest("Wallet top-up commits balance and history", () => {
                var service = new ReportService();
                Assert(service.TopUpWallet(3, 5000, "TienMat", out var error), error);
                Assert(Convert.ToDecimal(DatabaseHelper.ExecuteScalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=3")) == 15000m, "Wrong top-up balance");
            });
            RunTest("Overheat trigger and alert resolution", () => {
                DatabaseHelper.ExecuteNonQuery("UPDATE dbo.Pin SET NhietDo=65 WHERE MaPin='PIN-004'");
                Assert(Convert.ToInt32(DatabaseHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.NganTu n JOIN dbo.Pin p ON n.MaPinHienTai=p.MaPin WHERE p.MaPin='PIN-004' AND n.KhoaBaoTri=1 AND p.DongDien=0 AND p.TrangThai=N'LoiQuaNhiet'")) == 1, "Safety trigger failed");
                int id = Convert.ToInt32(DatabaseHelper.ExecuteScalar("SELECT MAX(MaCB) FROM dbo.CanhBaoIoT WHERE MaPin='PIN-004' AND DaXuLy=0"));
                Assert(new SafetyMonitorService().ResolveAlert(id, "test", "isolated integration test"), "Resolve failed");
                Assert(Convert.ToInt32(DatabaseHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaCB=" + id + " AND DaXuLy=1")) == 1, "Alert not resolved");
            });
            RunTest("Swap commits once and rejects stale retry", () => {
                var service = new SwapService();
                Assert(service.ValidateCustomerWallet("0987654321", out var customer, out _), "Customer missing");
                var result = service.ExecuteFullSwap(customer!, "TRAM-BK01", 6, "PIN-009", 18, 1, "PIN-001", 98.5m);
                Assert(result.Success, result.Message);
                var retry = service.ExecuteFullSwap(customer!, "TRAM-BK01", 6, "PIN-009", 18, 1, "PIN-001", 98.5m);
                Assert(!retry.Success, "Stale swap was accepted");
                Assert(Convert.ToDecimal(DatabaseHelper.ExecuteScalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=1")) == 125000m, "Wallet charged incorrectly");
            });
            RunTest("Emergency station cutoff stops every slot", () => {
                new SafetyMonitorService().EmergencyCutoffStation("TRAM-CG02");
                Assert(Convert.ToInt32(DatabaseHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.NganTu WHERE MaTram='TRAM-CG02' AND (KhoaBaoTri=0 OR CuaDangMo=1)")) == 0, "Slot not locked");
                Assert(Convert.ToInt32(DatabaseHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.Pin p JOIN dbo.NganTu n ON p.MaPin=n.MaPinHienTai WHERE n.MaTram='TRAM-CG02' AND p.DongDien<>0")) == 0, "Charging not stopped");
            });
            Console.WriteLine("\n===============================================================");
            Console.WriteLine($"VERIFICATION SUMMARY: {passed} PASSED, {failed} FAILED (TOTAL: {passed + failed})");
            Console.WriteLine("===============================================================");

            if (failed > 0)
            {
                Environment.Exit(1);
            }
        }

        static void RunTest(string testName, Action testAction)
        {
            try
            {
                testAction();
                passed++;
                Console.WriteLine($"[PASS] {testName}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[FAIL] {testName}: {ex.Message}");
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        static void Test_PasswordHelper_Hash()
        {
            string hash = PasswordHelper.HashPassword("123456");
            Assert(!string.IsNullOrEmpty(hash), "Hash cannot be null or empty");
            Assert(hash.Length == 64, $"Expected 64 characters, got {hash.Length}");
            Assert(hash == "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92", "Hash value does not match known SHA-256 for '123456'");
            Assert(PasswordHelper.HashPassword("") == "", "Empty string should return empty string");
        }

        static void Test_PasswordHelper_IsSha256()
        {
            Assert(PasswordHelper.IsSha256("8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92"), "Valid lowercase hex should return true");
            Assert(PasswordHelper.IsSha256("8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92"), "Valid uppercase hex should return true");
            Assert(!PasswordHelper.IsSha256("123456"), "Plaintext password should return false");
            Assert(!PasswordHelper.IsSha256(null), "Null should return false");
            Assert(!PasswordHelper.IsSha256(""), "Empty should return false");
            Assert(!PasswordHelper.IsSha256("8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c9Z"), "Invalid hex char should return false");
        }

        static void Test_PasswordHelper_Verify()
        {
            string hash = PasswordHelper.HashPassword("admin123");
            Assert(PasswordHelper.VerifyPassword("admin123", hash), "Should verify true against SHA-256 hash");
            Assert(!PasswordHelper.VerifyPassword("wrongpass", hash), "Should verify false against incorrect password");
            // Test seed fallback: plaintext stored matches input
            Assert(PasswordHelper.VerifyPassword("plainSeed123", "plainSeed123"), "Should verify true against plaintext seed");
        }

        static void Test_DatabaseHelper_PoolingAndTimeout()
        {
            Assert(DatabaseHelper.ConnectionString.Contains("Pooling=True"), "ConnectionString must contain Pooling=True");
            Assert(DatabaseHelper.ConnectionString.Contains("Max Pool Size"), "ConnectionString must configure Max Pool Size");
            Assert(DatabaseHelper.DefaultCommandTimeout == 30, $"DefaultCommandTimeout should be 30, was {DatabaseHelper.DefaultCommandTimeout}");

            using var conn = new SqlConnection(DatabaseHelper.ConnectionString);
            conn.Open();
            Assert(conn.State == ConnectionState.Open, "Connection failed to open with configured pooling string");
        }

        static void Test_Database_8Tables()
        {
            string[] requiredTables = { "TaiKhoan", "KhachHang", "TramPin", "NganTu", "Pin", "LichSuDoiPin", "LichSuNapTien", "CanhBaoIoT" };
            foreach (var table in requiredTables)
            {
                string sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @Tbl";
                var p = new SqlParameter("@Tbl", SqlDbType.VarChar) { Value = table };
                int count = Convert.ToInt32(DatabaseHelper.ExecuteScalar(sql, p));
                Assert(count == 1, $"Required table '{table}' is missing from BatterySwappingDB!");
            }
        }

        static void Test_Database_StoredProcedures()
        {
            string[] requiredProcedures = { "sp_ThucHienDoiPin", "sp_NapTienVi", "sp_LayChiTietTramVaNgan", "sp_BaoTriThayThePin", "sp_GiaiQuyetCanhBaoAnToan" };
            foreach (var sp in requiredProcedures)
            {
                string sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_NAME = @SP";
                var p = new SqlParameter("@SP", SqlDbType.VarChar) { Value = sp };
                int count = Convert.ToInt32(DatabaseHelper.ExecuteScalar(sql, p));
                Assert(count == 1, $"Stored procedure '{sp}' is missing from BatterySwappingDB!");
            }
        }

        static void Test_Database_Trigger()
        {
            string sql = "SELECT COUNT(*) FROM sys.triggers WHERE name = 'trg_KiemTraNhietDoPin' AND is_disabled = 0";
            int count = Convert.ToInt32(DatabaseHelper.ExecuteScalar(sql));
            Assert(count == 1, "Trigger 'trg_KiemTraNhietDoPin' is missing or disabled in BatterySwappingDB!");
        }

        static void Test_AuthService_AdminLogin()
        {
            var auth = new AuthService();
            bool ok = auth.Login("admin", "123456", out string error);
            Assert(ok, $"Admin login failed: {error}");
            Assert(AuthService.CurrentUser != null, "CurrentUser is null after successful login");
            Assert(AuthService.CurrentUser!.TenDangNhap == "admin", $"Expected 'admin', got '{AuthService.CurrentUser!.TenDangNhap}'");
            Assert(AuthService.CurrentUser!.VaiTro == "Admin", $"Expected role 'Admin', got '{AuthService.CurrentUser!.VaiTro}'");
        }

        static void Test_AuthService_TechLogin()
        {
            var auth = new AuthService();
            bool ok = auth.Login("tech01", "123456", out string error);
            Assert(ok, $"Technician login failed: {error}");
            Assert(AuthService.CurrentUser != null, "CurrentUser is null after technician login");
            Assert(AuthService.CurrentUser!.VaiTro == "KyThuatVien", $"Expected role 'KyThuatVien', got '{AuthService.CurrentUser!.VaiTro}'");
        }

        static void Test_AuthService_DriverLogin()
        {
            var auth = new AuthService();
            bool ok = auth.Login("driver01", "123456", out string error);
            Assert(ok, $"Driver login failed: {error}");
            Assert(AuthService.CurrentUser != null, "CurrentUser is null after driver login");
            Assert(AuthService.CurrentUser!.VaiTro == "KhachHang", $"Expected role 'KhachHang', got '{AuthService.CurrentUser!.VaiTro}'");
        }

        static void Test_AuthService_InvalidPassword()
        {
            var auth = new AuthService();
            bool ok = auth.Login("admin", "WrongPassword_999", out string error);
            Assert(!ok, "Login should fail with incorrect password");
            Assert(error.Contains("Mật khẩu không chính xác"), $"Unexpected error message: '{error}'");
        }

        static void Test_AuthService_NonExistentUser()
        {
            var auth = new AuthService();
            bool ok = auth.Login("ghost_user_12345", "123456", out string error);
            Assert(!ok, "Login should fail with non-existent user");
            Assert(error.Contains("không tồn tại"), $"Unexpected error message: '{error}'");
        }

        static void Test_StationService_GetAllStations()
        {
            var service = new StationService();
            var stations = service.GetAllStations();
            Assert(stations.Count >= 2, $"Expected at least 2 stations, found {stations.Count}");
            Assert(stations.Exists(s => s.MaTram == "TRAM-BK01"), "Station TRAM-BK01 not found");
            Assert(stations.Exists(s => s.MaTram == "TRAM-CG02"), "Station TRAM-CG02 not found");
        }

        static void Test_StationService_GetCabinetSlots()
        {
            var service = new StationService();
            var slots = service.GetCabinetSlots("TRAM-BK01");
            Assert(slots.Count == 8, $"Station TRAM-BK01 should have exactly 8 cabinet slots, found {slots.Count}");
            for (int i = 0; i < 8; i++)
            {
                Assert(slots[i].SoThuTu == i + 1, $"Slot index mismatch: expected {i + 1}, got {slots[i].SoThuTu}");
            }
        }

        static void Test_BatteryService_GetAllBatteries()
        {
            var service = new BatteryService();
            var batteries = service.GetAllBatteries();
            Assert(batteries.Count >= 14, $"Expected at least 14 batteries, found {batteries.Count}");
            foreach (var b in batteries)
            {
                Assert(!string.IsNullOrEmpty(b.MaPin), "MaPin cannot be null or empty");
                Assert(b.DungLuongThietKe > 0, "DungLuongThietKe must be > 0");
                Assert(b.SoC >= 0 && b.SoC <= 100, $"SoC out of range: {b.SoC}");
                Assert(b.SoH >= 0 && b.SoH <= 100, $"SoH out of range: {b.SoH}");
            }
        }

        static void Test_BatteryService_GetBatteryById()
        {
            var service = new BatteryService();
            var b = service.GetBatteryById("PIN-001");
            Assert(b != null, "Battery PIN-001 not found");
            Assert(b!.MaPin == "PIN-001", $"Expected PIN-001, got {b.MaPin}");
            Assert(b.LoaiPin.Contains("Lithium"), $"Unexpected battery chemistry: {b.LoaiPin}");
        }

        static void Test_BatteryService_InspectBattery()
        {
            var service = new BatteryService();
            var insp = service.InspectBattery("PIN-001");
            Assert(insp != null, "Inspection failed for PIN-001");
            Assert(insp!.Battery.MaPin == "PIN-001", "Inspected battery ID mismatch");
            Assert(!string.IsNullOrEmpty(insp.HealthStatusDescription), "HealthStatusDescription cannot be empty");
            Assert(!string.IsNullOrEmpty(insp.RecommendedAction), "RecommendedAction cannot be empty");
        }

        static void Test_QrCodeHelper_Roundtrip()
        {
            string payload = QrCodeHelper.GenerateCustomerQrPayload(1, "0901234567");
            Assert(payload.StartsWith("BSS:KH-1:0901234567:"), $"Invalid QR payload format: {payload}");
            bool parsed = QrCodeHelper.TryParseQrPayload(payload, out int maKH, out string phone);
            Assert(parsed, "Failed to parse generated QR payload");
            Assert(maKH == 1, $"Customer ID mismatch: expected 1, got {maKH}");
            Assert(phone == "0901234567", $"Phone mismatch: expected 0901234567, got {phone}");
        }

        static void Test_SwapService_WalletValidation()
        {
            var service = new SwapService();
            // Test non-existent phone
            bool ok = service.ValidateCustomerWallet("0999999999", out KhachHang? cust, out string error);
            Assert(!ok, "Should fail for unknown customer phone");
            Assert(error.Contains("Không tìm thấy"), $"Unexpected error message: {error}");

            // Test valid customer driver01 (0912345678)
            ok = service.ValidateCustomerWallet("0912345678", out cust, out error);
            Assert(ok, $"Customer validation failed: {error}");
            Assert(cust != null, "Customer object was null");
            Assert(cust!.SoDuVi >= SwapService.SWAP_FEE, "Customer balance should be sufficient for swap");
        }

        static void Test_SwapService_FindAvailableEmptySlot()
        {
            var service = new SwapService();
            var emptySlot = service.FindAvailableEmptySlot("TRAM-BK01");
            Assert(emptySlot != null, "No available empty slot found at TRAM-BK01");
            Assert(emptySlot!.MaPinHienTai == null, "Empty slot must not hold a battery");
            Assert(!emptySlot.KhoaBaoTri, "Empty slot must not be maintenance locked");
        }

        static void Test_SwapService_FindOptimalBatterySlot()
        {
            var service = new SwapService();
            var optSlot = service.FindOptimalBatterySlot("TRAM-BK01");
            Assert(optSlot != null, "No qualified optimal battery found at TRAM-BK01");
            Assert(!string.IsNullOrEmpty(optSlot!.MaPinHienTai), "Optimal slot must hold a battery");
            Assert(optSlot.SoC >= SwapService.MIN_REQUIRED_SOC, $"SoC ({optSlot.SoC}%) below minimum ({SwapService.MIN_REQUIRED_SOC}%)");
            Assert(optSlot.SoH >= SwapService.MIN_REQUIRED_SOH, $"SoH ({optSlot.SoH}%) below minimum ({SwapService.MIN_REQUIRED_SOH}%)");
            Assert(optSlot.NhietDo <= SwapService.MAX_SAFE_TEMP, $"Temperature ({optSlot.NhietDo}°C) exceeds safe limit ({SwapService.MAX_SAFE_TEMP}°C)");
        }

        static void Test_ReportService_GetSummaryStats()
        {
            var service = new ReportService();
            var stats = service.GetSummaryStats();
            Assert(stats != null, "Report stats should not be null");
            Assert(stats!.TongSoPin >= 14, $"Expected >= 14 total batteries, got {stats.TongSoPin}");
            Assert(stats.SoPinKhaDung >= 0, "SoPinKhaDung must be non-negative");
            Assert(stats.TongDoanhThu >= 0, "TongDoanhThu must be non-negative");
        }

        static void Test_SafetyMonitorService_Scan()
        {
            var monitor = new SafetyMonitorService();
            // Start monitoring with short interval
            monitor.StartMonitoring(500);
            System.Threading.Thread.Sleep(1200); // Allow at least one scan tick
            monitor.StopMonitoring();
            // If it executed without exceptions, test passes
        }

        static void Test_SimulatorWorker_Lifecycle()
        {
            var worker = new SimulatorWorker(DatabaseHelper.ConnectionString);
            bool logTriggered = false;
            worker.OnLogMessage += msg => logTriggered = true;

            worker.Start();
            Assert(worker.IsRunning, "Worker should be running after Start()");
            System.Threading.Thread.Sleep(1500);
            worker.Stop();
            Assert(!worker.IsRunning, "Worker should be stopped after Stop()");
            Assert(logTriggered, "Worker did not invoke OnLogMessage during lifecycle");
        }

        static void Test_Wpf_FrozenBrushes()
        {
            var pin = new Pin { NhietDo = 25.0m };
            Assert(pin.MauNhietDo.IsFrozen, "Pin.MauNhietDo brush must be frozen for thread safety");

            pin.NhietDo = 52.0m;
            Assert(pin.MauNhietDo.IsFrozen, "Amber warning brush must be frozen");

            pin.NhietDo = 60.0m;
            Assert(pin.MauNhietDo.IsFrozen, "Red critical alert brush must be frozen");

            var slot = new NganTu();
            Assert(slot.MauTrangThai.IsFrozen, "NganTu.MauTrangThai brush must be frozen");
            Assert(slot.NenKhoangTu.IsFrozen, "NganTu.NenKhoangTu brush must be frozen");
        }
    }
}
