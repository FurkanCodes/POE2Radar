using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using POE2Radar.Core;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class StdStringReaderTests
{
    [Fact]
    public void ReadStdString_ReadsInlineSmallString()
    {
        using var process = ProcessHandle.AttachToProcess(
            Environment.ProcessId,
            Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var value = NativeStdString.Inline("deactivated");

        Assert.Equal("deactivated", reader.ReadStdString(value.Address));
    }

    [Fact]
    public void ReadStdString_ReadsHeapBackedLargeString()
    {
        using var process = ProcessHandle.AttachToProcess(
            Environment.ProcessId,
            Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var value = NativeStdString.Heap("sanctum_completed");

        Assert.Equal("sanctum_completed", reader.ReadStdString(value.Address));
    }

    private sealed class NativeStdString : IDisposable
    {
        private readonly nint _heap;

        private NativeStdString(nint address, nint heap)
        {
            Address = address;
            _heap = heap;
        }

        public nint Address { get; }

        public static NativeStdString Inline(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            Assert.True(bytes.Length <= 15);
            var address = AllocateZeroed(0x20);
            Marshal.Copy(bytes, 0, address, bytes.Length);
            Marshal.WriteInt32(address + 0x10, bytes.Length);
            Marshal.WriteInt32(address + 0x18, 15);
            return new NativeStdString(address, 0);
        }

        public static NativeStdString Heap(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            Assert.True(bytes.Length > 15);
            var address = AllocateZeroed(0x20);
            var heap = AllocateZeroed(bytes.Length + 1);
            Marshal.Copy(bytes, 0, heap, bytes.Length);
            Marshal.WriteIntPtr(address, heap);
            Marshal.WriteInt32(address + 0x10, bytes.Length);
            Marshal.WriteInt32(address + 0x18, bytes.Length);
            return new NativeStdString(address, heap);
        }

        public void Dispose()
        {
            if (_heap != 0)
                Marshal.FreeHGlobal(_heap);
            Marshal.FreeHGlobal(Address);
        }

        private static nint AllocateZeroed(int bytes)
        {
            var address = Marshal.AllocHGlobal(bytes);
            for (var i = 0; i < bytes; i++)
                Marshal.WriteByte(address, i, 0);
            return address;
        }
    }
}
