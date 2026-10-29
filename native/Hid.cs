using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MouseBattery
{
    internal sealed class Device
    {
        public string Path, Product;
        public int Vendor, Pid, Page, Usage, Input, Output, Feature;
        public int Model { get { return Protocol.Match(Vendor, Pid, Page); } }
        public string Summary { get { return String.Format("{0:X4}:{1:X4} page={2:X4} usage={3:X4} in={4} out={5} feature={6} {7}",
            Vendor, Pid, Page, Usage, Input, Output, Feature, Product); } }
    }

    internal static class Hid
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Attributes { public int Size; public ushort Vendor, Product, Version; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Caps
        {
            public ushort Usage, Page, Input, Output, Feature;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons,
                OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
        }
        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes attr);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle h, [In, Out] byte[] data, int size);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] data, int size);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(SafeFileHandle h, IntPtr data, int size);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        public static List<Device> Enumerate()
        {
            List<Device> result = new List<Device>();
            Guid guid; HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            try
            {
                for (int index = 0; ; index++)
                {
                    InterfaceData item = new InterfaceData(); item.Size = Marshal.SizeOf(typeof(InterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref item))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        throw new Win32Exception();
                    }
                    int size;
                    SetupDiGetDeviceInterfaceDetail(set, ref item, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size < 8) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref item, detail, size, out size, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        using (SafeFileHandle h = Open(path, false))
                        {
                            if (h.IsInvalid) continue;
                            Attributes attr = new Attributes(); attr.Size = Marshal.SizeOf(typeof(Attributes));
                            if (!HidD_GetAttributes(h, ref attr)) continue;
                            // Only enumerate Ninjutso; never open system input collections for I/O.
                            if (attr.Vendor != 0x1915) continue;
                            IntPtr preparsed;
                            if (!HidD_GetPreparsedData(h, out preparsed)) continue;
                            Caps caps;
                            try { if (HidP_GetCaps(preparsed, out caps) != 0x110000) continue; }
                            finally { HidD_FreePreparsedData(preparsed); }
                            string product = "";
                            IntPtr text = Marshal.AllocHGlobal(512);
                            try { if (HidD_GetProductString(h, text, 512)) product = Marshal.PtrToStringUni(text); }
                            finally { Marshal.FreeHGlobal(text); }
                            result.Add(new Device { Path = path, Product = product, Vendor = attr.Vendor, Pid = attr.Product,
                                Page = caps.Page, Usage = caps.Usage, Input = caps.Input, Output = caps.Output, Feature = caps.Feature });
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        private static SafeFileHandle Open(string path, bool io)
        { return CreateFile(path, io ? 0xC0000000u : 0u, 3, IntPtr.Zero, 3, 0u, IntPtr.Zero); }

        public static Reading Query(Device device)
        {
            if (device.Model != 0) throw new ArgumentException("不支持的设备");
            using (SafeFileHandle h = Open(device.Path, true))
            {
                if (h.IsInvalid) throw new Win32Exception();
                byte[] request = Protocol.Request(device.Feature);
                if (!HidD_SetFeature(h, request, request.Length)) throw new Win32Exception();
                Thread.Sleep(100);
                byte[] reply = new byte[device.Feature]; reply[0] = 5;
                if (!HidD_GetFeature(h, reply, reply.Length)) throw new Win32Exception();
                return Protocol.ParseSora(reply);
            }
        }

        public static Reading[] Scan()
        { return Scan(null, true); }

        public static Reading[] Scan(Reading[] previous, bool readBattery)
        { return Scan(Enumerate(), previous, readBattery, Query); }

        internal static Reading[] Scan(List<Device> devices, Reading[] previous, bool readBattery, Func<Device, Reading> query)
        {
            Reading[] values = { Reading.Unknown("未检测到接收器") };
            foreach (Device device in devices)
            {
                int model = device.Model;
                if (model < 0 || values[model].Percent.HasValue) continue;
                if (!readBattery && previous != null && previous[model].Present)
                { values[model] = previous[model]; continue; }
                try { values[model] = query(device); values[model].Present = true; }
                catch (Exception ex) { Store.Log(device.Summary + " " + ex.Message); values[model] = Reading.Unknown("读取失败：请查看诊断", true); }
            }
            return values;
        }
    }
}
