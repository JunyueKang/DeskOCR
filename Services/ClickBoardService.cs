using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace OcrApp.Services
{
    public static class ClipboardService
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        public static bool TrySetText(string text, Window? ownerWindow = null)
        {
            IntPtr hwnd = ownerWindow != null ? new WindowInteropHelper(ownerWindow).Handle : IntPtr.Zero;
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");

            if (!OpenClipboard(hwnd) && !OpenClipboard(IntPtr.Zero))
            {
                return false;
            }

            try
            {
                EmptyClipboard();
                IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                if (hGlobal == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr ptr = GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }

                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                GlobalUnlock(hGlobal);

                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }

                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }


    }
}