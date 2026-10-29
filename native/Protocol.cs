using System;

namespace MouseBattery
{
    public sealed class Reading
    {
        public int? Percent;
        public bool Charging;
        public string Status;
        public Reading(int? percent, bool charging, string status)
        { Percent = percent; Charging = charging; Status = status; }
        public static Reading Unknown(string status) { return new Reading(null, false, status); }
        public override string ToString()
        { return Percent.HasValue ? Percent + "% · " + (Charging ? "充电中" : Status) : Status; }
    }

    public static class Protocol
    {
        public static readonly string[] Models = { "Sora V2", "ATK X1 Pro Max" };
        private static readonly int[] AtkIds = { 0x1017, 0x101A, 0x101B, 0x1031, 0x1159,
            0x11A7, 0x1283, 0x11D9, 0x1278, 0x11FE, 0x127A, 0x11FD, 0x1279 };

        public static int Match(int vendor, int product, int page)
        {
            if (vendor == 0x1915 && (product == 0xAE1C || product == 0xAE11) && page == 0xFFA0) return 0;
            if (vendor == 0x373B && Array.IndexOf(AtkIds, product) >= 0 && (page == 0xFF02 || page == 0xFF04)) return 1;
            return -1;
        }

        public static byte[] Request(int model, int length)
        {
            if (length < (model == 0 ? 32 : 17)) throw new ArgumentException("HID 报告长度不足");
            byte[] data = new byte[length];
            if (model == 0) { data[0] = 5; data[1] = 0x15; data[4] = 1; }
            else { data[0] = 8; data[1] = 4; data[16] = 0x49; }
            return data;
        }

        public static Reading ParseSora(byte[] data)
        {
            if (data.Length < 13 || data[0] != 5) throw new FormatException("Sora 报告编号或长度错误");
            int percent = data[9];
            if (percent > 100 || data[10] > 1 || data[11] > 1 || data[12] > 1)
                throw new FormatException("Sora 电量或状态无效");
            if (data[10] == 0 && data[11] == 0 && data[12] == 0) return Reading.Unknown("休眠或离线");
            return new Reading(data[11] == 1 ? 100 : percent, data[10] == 1,
                data[11] == 1 ? "已充满" : "在线");
        }

        public static Reading ParseAtk(byte[] data)
        {
            if (data.Length < 10 || data[0] != 8 || data[1] != 4) throw new FormatException("ATK 响应编号或长度错误");
            if (data[6] > 100 || data[7] > 1) throw new FormatException("ATK 电量或状态无效");
            if (data.Length >= 17)
            {
                int sum = 0; for (int i = 0; i < 17; i++) sum += data[i];
                if ((sum & 255) != 0x55) throw new FormatException("ATK 电量响应校验失败");
            }
            return new Reading(data[6], data[7] == 1, "在线");
        }
    }
}
