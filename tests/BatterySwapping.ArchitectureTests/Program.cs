using BatterySwapping.Core;

var hash = Passwords.Hash("123456");
Check(hash != Passwords.Hash("123456"), "salt is random");
Check(Passwords.Verify("123456", hash), "PBKDF2 roundtrip");
Check(!Passwords.Verify(hash, hash), "hash is not a password");
Check(!Passwords.Verify("bad", hash), "wrong password denied");

if(args.Contains("--business") || args.Contains("--wallet")) BusinessRegression.Run();
if(args.Contains("--cooldown")) CooldownRegression.Run();
if(args.Contains("--simulation")) SimulationRegression.Run();
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
