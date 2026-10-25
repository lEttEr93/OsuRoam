using System.Runtime.InteropServices;
using System.Text;

namespace OsuRoam.Services;

internal static class SecretProtector
{
    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        DATA_BLOB input = createBlob(Encoding.UTF8.GetBytes(value));
        try
        {
            if (!CryptProtectData(ref input, "OsuRoam OAuth secret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out DATA_BLOB output))
                throw new InvalidOperationException($"DPAPI encryption failed ({Marshal.GetLastWin32Error()}).");

            try
            {
                byte[] encrypted = new byte[output.cbData];
                Marshal.Copy(output.pbData, encrypted, 0, output.cbData);
                return Convert.ToBase64String(encrypted);
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            return string.Empty;

        try
        {
            DATA_BLOB input = createBlob(Convert.FromBase64String(protectedValue));
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out DATA_BLOB output))
                    return string.Empty;

                try
                {
                    byte[] decrypted = new byte[output.cbData];
                    Marshal.Copy(output.pbData, decrypted, 0, output.cbData);
                    return Encoding.UTF8.GetString(decrypted);
                }
                finally
                {
                    LocalFree(output.pbData);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
            }
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static DATA_BLOB createBlob(byte[] bytes)
    {
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DATA_BLOB { cbData = bytes.Length, pbData = pointer };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DATA_BLOB dataIn, string description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB dataIn, IntPtr description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
