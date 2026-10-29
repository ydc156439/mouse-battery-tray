using System;
using MouseBattery;

internal static class ProtocolTests
{
    private static int count;
    private static void Check(bool success, string name)
    { if (!success) throw new Exception("FAILED: " + name); count++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (FormatException) { rejected = true; } Check(rejected, name); }
    private static byte[] Sora(int percent, int charge, int full, int online)
    { byte[] d = new byte[32]; d[0] = 5; d[9] = (byte)percent; d[10] = (byte)charge; d[11] = (byte)full; d[12] = (byte)online; return d; }
    private static byte[] Atk(int percent, int charge)
    { byte[] d = new byte[17]; d[0] = 8; d[1] = 4; d[6] = (byte)percent; d[7] = (byte)charge;
      int sum = 0; for (int i = 0; i < 16; i++) sum += d[i]; d[16] = (byte)((0x55 - sum) & 255); return d; }
    public static void Main()
    {
        Check(Protocol.Match(0x1915, 0xAE1C, 0xFFA0) == 0, "Sora wireless ID");
        Check(Protocol.Match(0x1915, 0xAE11, 0xFFA0) == 0, "Sora wired ID");
        Check(Protocol.Match(0x373B, 0x1017, 0xFF02) == 1, "ATK X1 Pro Max ID");
        Check(Protocol.Match(0x373B, 0x101A, 0xFF02) == 1, "ATK 1K receiver");
        Check(Protocol.Match(0x373B, 0x101B, 0xFF04) == 1, "ATK 8K receiver");
        Check(Protocol.Match(0x373B, 0x1017, 1) == -1, "Skip system input interface");
        Check(Protocol.Match(0x373B, 0xFFFF, 0xFF02) == -1, "Skip unknown PID");
        Check(Protocol.Match(0x1111, 0x1017, 0xFF02) == -1, "Skip unknown vendor");
        byte[] request = Protocol.Request(0, 32);
        Check(request.Length == 32 && request[0] == 5 && request[1] == 0x15 && request[4] == 1, "Sora query packet");
        request = Protocol.Request(1, 17);
        Check(request.Length == 17 && request[0] == 8 && request[1] == 4 && request[16] == 0x49, "ATK query packet");
        Check(Protocol.Request(1, 64)[63] == 0, "Output report padding");
        Check(Protocol.ParseSora(Sora(72, 0, 0, 1)).Percent == 72, "Sora battery value");
        Check(Protocol.ParseSora(Sora(72, 1, 0, 1)).Charging, "Sora charging");
        Check(Protocol.ParseSora(Sora(98, 0, 1, 1)).Percent == 100, "Sora fully charged");
        Check(!Protocol.ParseSora(Sora(72, 0, 0, 0)).Percent.HasValue, "Offline is unknown");
        Check(Protocol.ParseSora(Sora(0, 0, 0, 1)).Percent == 0, "Sora true zero battery");
        Reject(delegate { Protocol.ParseSora(new byte[5]); }, "Reject short Sora response");
        Reject(delegate { Protocol.ParseSora(Sora(101, 0, 0, 1)); }, "Reject invalid Sora value");
        Reject(delegate { Protocol.ParseSora(Sora(50, 2, 0, 1)); }, "Reject invalid Sora flags");
        Check(Protocol.ParseAtk(Atk(64, 0)).Percent == 64, "ATK battery value");
        Check(Protocol.ParseAtk(Atk(64, 1)).Charging, "ATK charging");
        Check(Protocol.ParseAtk(Atk(0, 0)).Percent == 0, "ATK true zero battery");
        Reject(delegate { Protocol.ParseAtk(new byte[9]); }, "Reject short ATK response");
        Reject(delegate { Protocol.ParseAtk(Atk(101, 0)); }, "Reject invalid ATK value");
        Reject(delegate { Protocol.ParseAtk(Atk(50, 2)); }, "Reject invalid ATK flags");
        byte[] corrupt = Atk(64, 0); corrupt[6] = 65;
        Reject(delegate { Protocol.ParseAtk(corrupt); }, "Reject corrupt checksum");
        byte[] unrelated = Atk(50, 0); unrelated[1] = 7;
        Reject(delegate { Protocol.ParseAtk(unrelated); }, "Reject unrelated response");
        Console.WriteLine(count + " tests passed.");
    }
}
