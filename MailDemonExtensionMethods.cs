using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Configuration;

using MimeKit;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace MailDemon
{
    public static class MailDemonExtensionMethods
    {
        public static Encoding Utf8EncodingNoByteMarker { get; } = new UTF8Encoding(false);

        /// <summary>
        /// Taken from https://www.regular-expressions.info/email.html
        /// </summary>
        private static readonly Regex validEmailRegex = new Regex(@"\A(?=[a-z0-9@.!#$%&'*+/=?^_`{|}~-]{6,254}\z)(?=[a-z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@)[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:(?=[a-z0-9-]{1,63}\.)[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?=[a-z0-9-]{1,63}\z)[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\z", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        public static SecureString ToSecureString(this string s)
        {
            SecureString ss = new SecureString();
            foreach (char c in s)
            {
                ss.AppendChar(c);
            }
            return ss;
        }

        public static async Task TimeoutAfter(this Task task, int milliseconds)
        {
            using CancellationTokenSource timeoutCancellationTokenSource = new CancellationTokenSource();
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

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int milliseconds)
        {
            using CancellationTokenSource timeoutCancellationTokenSource = new CancellationTokenSource();
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

        public static T GetValue<T>(this IConfiguration config, string key, T defaultValue)
        {
            string value = config[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Get remote ip address, optionally allowing for x-forwarded-for header check
        /// </summary>
        /// <param name="context">Http context</param>
        /// <param name="allowForwarded">Whether to allow x-forwarded-for header check</param>
        /// <returns>IPAddress</returns>
        public static System.Net.IPAddress GetRemoteIPAddress(this HttpContext context, bool allowForwarded = true)
        {
            if (allowForwarded)
            {
                string header = (context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault());
                if (System.Net.IPAddress.TryParse(header, out System.Net.IPAddress ip))
                {
                    return ip;
                }
            }
            return context.Connection.RemoteIpAddress;
        }

        /// <summary>
        /// Attempt to parse an email address. Email must have a @ in it to be valid. MailboxAddress.TryParse does not require the @.
        /// </summary>
        /// <param name="emailAddress">Email address</param>
        /// <param name="address">Receives the parsed email address</param>
        /// <returns>True if email is valid, false if not</returns>
        public static bool TryParseEmailAddress(this string emailAddress, out MailboxAddress address)
        {
            if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@') || !MailboxAddress.TryParse(emailAddress, out address))
            {
                address = default;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get domain from an email address
        /// </summary>
        /// <param name="text">Text</param>
        /// <returns>Domain</returns>
        public static string GetDomainFromEmailAddress(this string emailAddress)
        {
            int pos = emailAddress.IndexOf('@');
            return emailAddress.Substring(++pos);
        }

        /// <summary>
        /// Get utf8 bytes from string
        /// </summary>
        /// <param name="text">String</param>
        /// <returns>Utf8 bytes</returns>
        public static byte[] ToUtf8Bytes(this string text)
        {
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        /// <summary>
        /// Get string from utf8 bytes
        /// </summary>
        /// <param name="bytes">Utf8 bytes</param>
        /// <returns>String</returns>
        public static string ToUtf8String(this byte[] bytes)
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Format text for html
        /// </summary>
        /// <param name="text">Text</param>
        /// <param name="format">Format args</param>
        /// <returns>Formatted text</returns>
        public static string FormatHtml(this string text, params object[] format)
        {
            return string.Format((text ?? string.Empty).Replace("\n", "<br/>"), format);
        }

        /// <summary>
        /// Check if a view exists
        /// </summary>
        /// <param name="helper">Html helper</param>
        /// <param name="viewName">View name</param>
        /// <returns>True if exists, false otherwise</returns>
        public static bool ViewExists(this IHtmlHelper helper, string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
            {
                return false;
            }
            var viewEngine = helper.ViewContext.HttpContext.RequestServices.GetService(typeof(ICompositeViewEngine)) as ICompositeViewEngine;
            var view = viewEngine.FindView(helper.ViewContext, viewName, false);
            return view.View != null;
        }

        private static RSACryptoServiceProvider GetRSAProviderForPrivateKey(string pemPrivateKey)
        {
            RSACryptoServiceProvider rsaKey = new RSACryptoServiceProvider();
            PemReader reader = new PemReader(new StringReader(pemPrivateKey));
            RsaPrivateCrtKeyParameters rkp = reader.ReadObject() as RsaPrivateCrtKeyParameters;
            RSAParameters rsaParameters = DotNetUtilities.ToRSAParameters(rkp);
            rsaKey.ImportParameters(rsaParameters);
            return rsaKey;
        }

        /// <summary>
        /// Load an ssl certificate from .pem files
        /// </summary>
        /// <param name="publicKeyFile">Public key file</param>
        /// <param name="privateKeyFile">Private key file</param>
        /// <param name="password">Password</param>
        /// <returns>X509Certificate2 or null if error</returns>
        public static async Task<X509Certificate2> LoadSslCertificateAsync(string publicKeyFile, string privateKeyFile, SecureString password)
        {
            // if missing files, no certificate possible
            if (!File.Exists(publicKeyFile) || (!string.IsNullOrWhiteSpace(privateKeyFile) && !File.Exists(privateKeyFile)))
            {
                return null;
            }

            Exception error = null;
            if (!string.IsNullOrWhiteSpace(publicKeyFile))
            {
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(publicKeyFile);
                        X509Certificate2 newSslCertificate = (password == null ? new X509Certificate2(bytes) : new X509Certificate2(bytes, password));
                        if (!newSslCertificate.HasPrivateKey && !string.IsNullOrWhiteSpace(privateKeyFile))
                        {
                            X509Certificate2 copiedCert = newSslCertificate.CopyWithPrivateKey(GetRSAProviderForPrivateKey(await File.ReadAllTextAsync(privateKeyFile)));
                            newSslCertificate.Dispose();
                            newSslCertificate = copiedCert;
                        }
                        MailDemonLog.Debug("Loaded ssl certificate {0}", newSslCertificate);
                        return newSslCertificate;
                    }
                    catch (Exception ex)
                    {
                        error = ex;

                        // in case something is copying a new certificate, give it a second and try one more time
                        Thread.Sleep(1000);
                    }
                }
            }
            if (error != null)
            {
                MailDemonLog.Error("Error loading ssl certificate: {0}", error);
            }
            return null;
        }

        /// <summary>
        /// Async wait
        /// </summary>
        /// <param name="handle">Wait handle</param>
        /// <param name="timeout">Timeout</param>
        /// <param name="cancellationToken">Cancel token</param>
        /// <returns>True if cancel/signalled, false otherwise</returns>
        public static async Task<bool> WaitOneAsync(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RegisteredWaitHandle registeredHandle = null;
            CancellationTokenRegistration tokenRegistration = default;
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                registeredHandle = ThreadPool.RegisterWaitForSingleObject(
                    handle,
                    (state, timedOut) => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut),
                    tcs,
                    timeout,
                    true);
                tokenRegistration = cancellationToken.Register(
                    state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
                    tcs);
                return await tcs.Task;
            }
            finally
            {
                if (registeredHandle != null)
                {
                    registeredHandle.Unregister(null);
                }
                tokenRegistration.Dispose();
            }
        }

        /// <summary>
        /// Make a task execute synchronously
        /// </summary>
        /// <param name="task">Task</param>
        public static void Sync(this Task task)
        {
            task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Make a task execute synchronously
        /// </summary>
        /// <param name="task">Task</param>
        /// <returns>Result</returns>
        public static T Sync<T>(this Task<T> task)
        {
            return task.ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
