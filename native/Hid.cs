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
        [StructLayout(LayoutKind.Sequential)]
        private struct OverlappedData
        { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }

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
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int count, out int transferred, IntPtr overlap);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out int transferred, IntPtr overlap);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlap, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlap);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr security, bool manual, bool initial, IntPtr name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr h, uint timeout);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

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
                            // Only enumerate the two relevant vendors; never open system input collections for I/O.
                            if (attr.Vendor != 0x1915 && attr.Vendor != 0x373B) continue;
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
        { return CreateFile(path, io ? 0xC0000000u : 0u, 3, IntPtr.Zero, 3, io ? 0x40000000u : 0u, IntPtr.Zero); }

        // Keep unmanaged buffers and OVERLAPPED alive until cancellation/completion has finished.
        private static int Transfer(SafeFileHandle handle, byte[] data, bool write, int timeout)
        {
            IntPtr signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (signal == IntPtr.Zero) throw new Win32Exception();
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            IntPtr overlap = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OverlappedData)));
            bool pending = false;
            try
            {
                Marshal.StructureToPtr(new OverlappedData { Event = signal }, overlap, false);
                Marshal.Copy(data, 0, buffer, data.Length);
                int count;
                bool success = write ? WriteFile(handle, buffer, data.Length, out count, overlap)
                                     : ReadFile(handle, buffer, data.Length, out count, overlap);
                if (!success)
                {
                    if (Marshal.GetLastWin32Error() != 997) throw new Win32Exception();
                    pending = true;
                    uint wait = WaitForSingleObject(signal, (uint)timeout);
                    if (wait != 0)
                    {
                        CancelIoEx(handle, overlap);
                        GetOverlappedResult(handle, overlap, out count, true);
                        pending = false;
                        if (wait != 258) throw new Win32Exception();
                        return 0;
                    }
                    success = GetOverlappedResult(handle, overlap, out count, true);
                    pending = false;
                    if (!success) throw new Win32Exception();
                }
                if (!write && count > 0) Marshal.Copy(buffer, data, 0, count);
                return count;
            }
            finally
            {
                if (pending) { int count; CancelIoEx(handle, overlap); GetOverlappedResult(handle, overlap, out count, true); }
                Marshal.FreeHGlobal(overlap); Marshal.FreeHGlobal(buffer); CloseHandle(signal);
            }
        }

        public static Reading Query(Device device)
        {
            using (SafeFileHandle h = Open(device.Path, true))
            {
                if (h.IsInvalid) throw new Win32Exception();
                if (device.Model == 0)
                {
                    byte[] request = Protocol.Request(0, device.Feature);
                    if (!HidD_SetFeature(h, request, request.Length)) throw new Win32Exception();
                    Thread.Sleep(100);
                    byte[] reply = new byte[device.Feature]; reply[0] = 5;
                    if (!HidD_GetFeature(h, reply, reply.Length)) throw new Win32Exception();
                    return Protocol.ParseSora(reply);
                }
                byte[] output = Protocol.Request(1, device.Output);
                if (device.Input < 10) throw new FormatException("ATK 接口无法容纳电量响应");
                byte[] input = new byte[device.Input];
                // Bounded draining of queued events before requesting a fresh battery report.
                DateTime drain = DateTime.UtcNow.AddMilliseconds(100);
                while (DateTime.UtcNow < drain && Transfer(h, input, false, 1) > 0) { }
                if (Transfer(h, output, true, 800) != output.Length) throw new TimeoutException("ATK 请求发送超时");
                DateTime until = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < until)
                {
                    int length = Transfer(h, input, false, 50);
                    if (length >= 2 && input[0] == 8 && input[1] == 4)
                    { byte[] reply = new byte[length]; Array.Copy(input, reply, length); return Protocol.ParseAtk(reply); }
                }
                return Reading.Unknown("无响应：可能休眠或被驱动占用");
            }
        }

        public static Reading[] Scan()
        {
            Reading[] values = { Reading.Unknown("未检测到接收器"), Reading.Unknown("未检测到接收器") };
            List<Device> devices = Enumerate();
            devices.Sort(delegate(Device a, Device b) { return Priority(a).CompareTo(Priority(b)); });
            foreach (Device device in devices)
            {
                int model = device.Model;
                if (model < 0 || values[model].Percent.HasValue) continue;
                try { values[model] = Query(device); }
                catch (Exception ex) { Store.Log(device.Summary + " " + ex.Message); values[model] = Reading.Unknown("读取失败：请查看诊断"); }
            }
            return values;
        }
        private static int Priority(Device d) { return (d.Usage == 2 ? 0 : 2) + (d.Page == 0xFF02 ? 0 : 1); }
    }
}
