using System;

namespace MouseBattery
{
    public sealed class Reading
    {
        public int? Percent;
        public bool Charging;
        public string Status;
        public bool Present;
        public Reading(int? percent, bool charging, string status)
        { Percent = percent; Charging = charging; Status = status; Present = true; }
        public static Reading Unknown(string status) { return Unknown(status, false); }
        public static Reading Unknown(string status, bool present)
        { Reading value = new Reading(null, false, status); value.Present = present; return value; }
        public override string ToString()
        { return Percent.HasValue ? Percent + "% · " + (Charging ? "充电中" : Status) : Status; }
    }

    public static class Protocol
    {
        public static readonly string[] Models = { "Sora V2" };
        public static int Match(int vendor, int product, int page)
        {
            if (vendor == 0x1915 && (product == 0xAE1C || product == 0xAE11) && page == 0xFFA0) return 0;
            return -1;
        }

        public static byte[] Request(int length)
        {
            if (length < 32) throw new ArgumentException("HID 报告长度不足");
            byte[] data = new byte[length];
            data[0] = 5; data[1] = 0x15; data[4] = 1;
            return data;
        }

        public static Reading ParseSora(byte[] data)
        {
            if (data.Length < 13 || data[0] != 5) throw new FormatException("Sora 报告编号或长度错误");
            int percent = data[9];
            if (percent > 100 || data[10] > 1 || data[11] > 1 || data[12] > 1)
                throw new FormatException("Sora 电量或状态无效");
            if (data[10] == 0 && data[11] == 0 && data[12] == 0) return Reading.Unknown("休眠或离线", true);
            return new Reading(data[11] == 1 ? 100 : percent, data[10] == 1,
                data[11] == 1 ? "已充满" : "在线");
        }

    }
}
