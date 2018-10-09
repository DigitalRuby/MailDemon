using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace MailDemon
{
    public static class MailDemonExtensionMethods
    {
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

        public static async Task<bool> TryAwait(this Task task, int timeoutMilliseconds)
        {
            Task<Task> completed = Task.WhenAny(task, Task.Delay(timeoutMilliseconds));
            await completed;
            return (completed == task);
        }

        public static async Task<bool> TryAwait<T>(this Task<T> task, int timeoutMilliseconds)
        {
            Task<Task> completed = Task.WhenAny(task, Task.Delay(timeoutMilliseconds));
            await completed;
            return (completed.Result == task);
        }
    }
}
