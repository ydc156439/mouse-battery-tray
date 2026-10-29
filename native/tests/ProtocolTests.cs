using System;
using System.Collections.Generic;
using System.IO;
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
    public static void Main(string[] args)
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
        Check(Protocol.ParseSora(Sora(72, 0, 0, 0)).Present, "Sleeping mouse still has receiver");
        Check(!Reading.Unknown("not inserted").Present, "Missing receiver is absent");
        Device sora = new Device { Vendor = 0x1915, Pid = 0xAE1C, Page = 0xFFA0 };
        Device atk = new Device { Vendor = 0x373B, Pid = 0x1017, Page = 0xFF02, Usage = 2 };
        int queries = 0;
        Func<Device, Reading> query = delegate(Device d) { queries++; return new Reading(d.Model == 0 ? 72 : 64, false, "online"); };
        Reading[] values = Hid.Scan(new List<Device>(), null, true, query);
        Check(!values[0].Present && !values[1].Present && queries == 0, "No inserted devices yields no visible entries or queries");
        values = Hid.Scan(new List<Device> { sora }, values, false, query);
        Check(values[0].Present && !values[1].Present && queries == 1, "Insert Sora queries immediately");
        values = Hid.Scan(new List<Device> { sora }, values, false, query);
        Check(values[0].Percent == 72 && queries == 1, "Presence polling does not repeat battery request");
        values = Hid.Scan(new List<Device> { sora, atk }, values, false, query);
        Check(values[0].Present && values[1].Present && queries == 2, "Insert ATK shows second entry and queries only new device");
        values = Hid.Scan(new List<Device> { atk }, values, false, query);
        Check(!values[0].Present && !values[0].Percent.HasValue && values[1].Present, "Unplug Sora removes entry and stale battery");
        values = Hid.Scan(new List<Device>(), values, false, query);
        Check(!values[0].Present && !values[1].Present, "Unplug both clears all entries");
        values = Hid.Scan(new List<Device> { sora }, values, false, delegate(Device d) { return Reading.Unknown("sleeping"); });
        Check(values[0].Present && !values[0].Percent.HasValue, "Inserted sleeping device remains visible without fabricated percent");
        values = Hid.Scan(new List<Device> { atk }, values, true, delegate(Device d) { throw new Exception("busy"); });
        Check(values[1].Present && !values[1].Percent.HasValue, "Inserted busy device remains visible with unknown battery");
        bool[,] digits = TrayDigits.Render(72);
        // 7 on the left and 2 on the right must both be present in one line.
        Check(digits[1, 1] && digits[5, 1] && digits[9, 1] && digits[13, 1], "Two-digit top row contains both glyphs");
        Check(digits[9, 13] && digits[13, 13], "Second digit bottom row is not clipped");
        digits = TrayDigits.Render(100);
        Check(digits[3, 1] && digits[6, 1] && digits[10, 1], "100 contains all three digits on one line");
        Check(digits[4, 13] && digits[8, 13] && digits[12, 13], "100 complete bottom row");
        bool fits = true;
        for (int value = 0; value <= 100; value++)
        {
            digits = TrayDigits.Render(value); int filled = 0;
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                if (digits[x, y]) filled++;
                if ((x == 0 || x == 15 || y == 0 || y == 15) && digits[x, y]) fits = false;
            }
            if (filled == 0) fits = false;
        }
        Check(fits, "All values 0-100 stay inside the tray grid with a safety margin");
        digits = TrayDigits.Render(null);
        Check(digits[1, 7] && digits[13, 7] && !digits[1, 1], "Unknown value keeps two visible dashes");
        Console.WriteLine(count + " tests passed.");
        if (args.Length == 1)
        {
            Directory.CreateDirectory(args[0]);
            foreach (int value in new int[] { 0, 9, 10, 20, 72, 99, 100 })
            {
                digits = TrayDigits.Render(value);
                using (StreamWriter writer = new StreamWriter(Path.Combine(args[0], value + ".ppm")))
                {
                    writer.WriteLine("P3\n16 16\n255");
                    for (int y = 0; y < 16; y++)
                    { for (int x = 0; x < 16; x++) writer.Write(digits[x, y] ? "235 241 250 " : "24 32 44 "); writer.WriteLine(); }
                }
            }
        }
    }
}
