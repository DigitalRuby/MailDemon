using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace MailDemon
{
    public class MailDemonLogProvider : ILogger, ILoggerProvider
    {
        private NLog.Logger logger;

        public void Dispose()
        {
        }

        /// <summary>
        /// Map Microsoft log level to NLog log level
        /// </summary>
        /// <param name="logLevel">Microsoft log level</param>
        /// <returns>NLog log level</returns>
        public static NLog.LogLevel GetNLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            switch (logLevel)
            {
                case Microsoft.Extensions.Logging.LogLevel.Critical: return NLog.LogLevel.Fatal;
                case Microsoft.Extensions.Logging.LogLevel.Debug: return NLog.LogLevel.Debug;
                case Microsoft.Extensions.Logging.LogLevel.Error: return NLog.LogLevel.Error;
                case Microsoft.Extensions.Logging.LogLevel.Information: return NLog.LogLevel.Info;
                case Microsoft.Extensions.Logging.LogLevel.Trace: return NLog.LogLevel.Trace;
                case Microsoft.Extensions.Logging.LogLevel.Warning: return NLog.LogLevel.Warn;
                default: return NLog.LogLevel.Off;
            }
        }

        void ILogger.Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logger != null)
            {
                var nlogLevel = GetNLogLevel(logLevel);
                string logText = formatter(state, exception);
                logger.Log(nlogLevel, logText + (exception == null ? string.Empty : Environment.NewLine + exception.ToString()));
            }
        }

        bool ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            if (logger == null)
            {
                return false;
            }
            var nlogLevel = GetNLogLevel(logLevel);
            return logger.IsEnabled(nlogLevel);
        }

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return null;
        }

        ILogger ILoggerProvider.CreateLogger(string categoryName)
        {
            try
            {
                MailDemonLogProvider logger = new MailDemonLogProvider
                {
                    logger = NLog.LogManager.GetCurrentClassLogger()
                };
                return logger;
            }
            catch
            {
                return null;
            }
        }
    }
}
