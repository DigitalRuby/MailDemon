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

        void ILogger.Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logger != null)
            {
                var nlogLevel = MailDemonLog.GetNLogLevel(logLevel);
                string logText = formatter(state, exception);
                string stackTrace = (logText.Contains("antiforgery") ? Environment.StackTrace : (exception == null ? string.Empty : exception.ToString()));
                logger.Log(nlogLevel, logText + (Environment.NewLine + stackTrace).Trim());
            }
        }

        bool ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            if (logger == null)
            {
                return false;
            }
            var nlogLevel = MailDemonLog.GetNLogLevel(logLevel);
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
