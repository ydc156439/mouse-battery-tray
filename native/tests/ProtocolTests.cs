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
    public static void Main(string[] args)
    {
        Check(Protocol.Match(0x1915, 0xAE1C, 0xFFA0) == 0, "Sora wireless ID");
        Check(Protocol.Match(0x1915, 0xAE11, 0xFFA0) == 0, "Sora wired ID");
        Check(Protocol.Models.Length == 1 && Protocol.Models[0] == "Sora V2", "Only Sora V2 is exposed");
        Check(Protocol.Match(0x373B, 0x1017, 0xFF02) == -1, "Reject other mouse model");
        Check(Protocol.Match(0x373B, 0x101A, 0xFF02) == -1, "Reject other receiver");
        Check(Protocol.Match(0x1915, 0xAE1C, 1) == -1, "Skip system input interface");
        Check(Protocol.Match(0x1915, 0xFFFF, 0xFFA0) == -1, "Skip unknown PID");
        Check(Protocol.Match(0x1111, 0xAE1C, 0xFFA0) == -1, "Skip unknown vendor");
        byte[] request = Protocol.Request(32);
        Check(request.Length == 32 && request[0] == 5 && request[1] == 0x15 && request[4] == 1, "Sora query packet");
        Check(Protocol.Request(64)[63] == 0, "Feature report padding");
        Check(Protocol.ParseSora(Sora(72, 0, 0, 1)).Percent == 72, "Sora battery value");
        Check(Protocol.ParseSora(Sora(72, 1, 0, 1)).Charging, "Sora charging");
        Check(Protocol.ParseSora(Sora(98, 0, 1, 1)).Percent == 100, "Sora fully charged");
        Check(!Protocol.ParseSora(Sora(72, 0, 0, 0)).Percent.HasValue, "Offline is unknown");
        Check(Protocol.ParseSora(Sora(0, 0, 0, 1)).Percent == 0, "Sora true zero battery");
        Reject(delegate { Protocol.ParseSora(new byte[5]); }, "Reject short Sora response");
        Reject(delegate { Protocol.ParseSora(Sora(101, 0, 0, 1)); }, "Reject invalid Sora value");
        Reject(delegate { Protocol.ParseSora(Sora(50, 2, 0, 1)); }, "Reject invalid Sora flags");
        Check(Protocol.ParseSora(Sora(72, 0, 0, 0)).Present, "Sleeping mouse still has receiver");
        Check(!Reading.Unknown("not inserted").Present, "Missing receiver is absent");
        Device sora = new Device { Vendor = 0x1915, Pid = 0xAE1C, Page = 0xFFA0 };
        Device unrelated = new Device { Vendor = 0x373B, Pid = 0x1017, Page = 0xFF02, Usage = 2 };
        int queries = 0;
        Func<Device, Reading> query = delegate(Device d) { queries++; return new Reading(72, false, "online"); };
        Reading[] values = Hid.Scan(new List<Device>(), null, true, query);
        Check(values.Length == 1 && !values[0].Present && queries == 0, "No inserted device yields no entry or query");
        values = Hid.Scan(new List<Device> { unrelated }, values, true, query);
        Check(!values[0].Present && queries == 0, "Unrelated hardware never queried or displayed");
        values = Hid.Scan(new List<Device> { sora }, values, false, query);
        Check(values[0].Present && queries == 1, "Insert Sora queries immediately");
        values = Hid.Scan(new List<Device> { sora }, values, false, query);
        Check(values[0].Percent == 72 && queries == 1, "Presence polling does not repeat battery request");
        values = Hid.Scan(new List<Device> { sora, unrelated }, values, false, query);
        Check(values.Length == 1 && values[0].Present && queries == 1, "Other receiver adds no entry or request");
        values = Hid.Scan(new List<Device> { unrelated }, values, false, query);
        Check(!values[0].Present && !values[0].Percent.HasValue, "Unplug Sora clears entry even if other receiver remains");
        values = Hid.Scan(new List<Device>(), values, false, query);
        Check(!values[0].Present, "No devices clears all entries");
        values = Hid.Scan(new List<Device> { sora }, values, false, delegate(Device d) { return Reading.Unknown("sleeping"); });
        Check(values[0].Present && !values[0].Percent.HasValue, "Inserted sleeping device remains visible without fabricated percent");
        values = Hid.Scan(new List<Device> { sora }, values, true, delegate(Device d) { throw new Exception("busy"); });
        Check(values[0].Present && !values[0].Percent.HasValue, "Inserted busy device remains visible with unknown battery");
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
