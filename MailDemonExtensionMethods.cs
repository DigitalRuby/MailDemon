using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

namespace MailDemon
{
    public static class MailDemonExtensionMethods
    {
        public static Encoding Utf8EncodingNoByteMarker { get; } = new UTF8Encoding(false);

        public static string ToUnsecureString(this SecureString s)
        {
            if (s == null)
            {
                return null;
            }
            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(s);
                char[] buffer = new char[s.Length];
                Marshal.Copy(valuePtr, buffer, 0, s.Length);
                return new string(buffer);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }

        public static async Task TimeoutAfter(this Task task, int milliseconds)
        {
            using (CancellationTokenSource timeoutCancellationTokenSource = new CancellationTokenSource())
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(milliseconds, timeoutCancellationTokenSource.Token));
                if (completedTask == task)
                {
                    timeoutCancellationTokenSource.Cancel();
                    await task; // Very important in order to propagate exceptions
                }
                else
                {
                    throw new TimeoutException("The operation has timed out.");
                }
            }
        }

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int milliseconds)
        {
            using (CancellationTokenSource timeoutCancellationTokenSource = new CancellationTokenSource())
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(milliseconds, timeoutCancellationTokenSource.Token));
                if (completedTask == task)
                {
                    timeoutCancellationTokenSource.Cancel();
                    return await task; // Very important in order to propagate exceptions
                }
                else
                {
                    throw new TimeoutException("The operation has timed out.");
                }
            }
        }

        public static T GetValue<T>(this IConfiguration config, string key, T defaultValue)
        {
            string value = config[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
    }
}
