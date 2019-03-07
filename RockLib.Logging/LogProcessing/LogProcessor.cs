﻿using RockLib.Diagnostics;
using System;
using System.Diagnostics;

namespace RockLib.Logging.LogProcessing
{
    public abstract class LogProcessor : ILogProcessor
    {
        protected readonly static TraceSource TraceSource = Tracing.GetTraceSource(Logger.TraceSourceName);

        public bool IsDisposed { get; private set; }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing) => IsDisposed = true;

        public virtual void ProcessLogEntry(ILogger logger, LogEntry logEntry, Action<ErrorEventArgs> errorHandler)
        {
            if (IsDisposed)
                return;

            foreach (var contextProvider in logger.ContextProviders)
            {
                try 
                {
                    contextProvider.AddContext(logEntry);
                }
                catch (Exception ex)
                {
                    TraceSource.TraceEvent(TraceEventType.Warning, ex.HResult,
                        "[{0:s}] - Error while adding context to log entry {1} using context provider {2}.{3}{4}",
                        DateTime.Now, logEntry.UniqueId, contextProvider, Environment.NewLine, ex);

                    continue;
                }
            }

            foreach (var logProvider in logger.LogProviders)
            {
                if (logEntry.Level < logProvider.Level)
                    continue;

                try
                {
                    WriteToLogProvider(logProvider, logEntry, errorHandler, 0);
                }
                catch (Exception ex)
                {
                    HandleError(ex, logProvider, logEntry, errorHandler, 1,
                        "Error while sending log entry {0} to log provider {1}.", logEntry.UniqueId, logProvider);
                }
            }
        }

        protected abstract void WriteToLogProvider(ILogProvider logProvider, LogEntry logEntry,
            Action<ErrorEventArgs> errorHandler, int failureCount);

        protected void HandleError(Exception exception, ILogProvider logProvider, LogEntry logEntry,
            Action<ErrorEventArgs> errorHandler, int failureCount, string messageFormat, params object[] messageArgs)
        {
            TraceError(exception, messageFormat, messageArgs);

            if (errorHandler != null)
            {
                var args = new ErrorEventArgs(string.Format(messageFormat, messageArgs),
                    exception, logProvider, logEntry, failureCount);

                try
                {
                    errorHandler(args);
                }
                catch (Exception ex)
                {
                    TraceSource.TraceEvent(TraceEventType.Warning, ex.HResult,
                        "[{0:s}] - Error in error handler.{1}{2}",
                        DateTime.Now, Environment.NewLine, ex);
                }

                if (args.ShouldRetry)
                {
                    try
                    {
                        WriteToLogProvider(logProvider, logEntry, errorHandler, failureCount);
                    }
                    catch (Exception ex)
                    {
                        HandleError(ex, logProvider, logEntry, errorHandler, failureCount + 1,
                            "Error while re-sending log entry {0} to log provider {1}.", logEntry.UniqueId, logProvider);
                    }
                }
            }
        }

        private static void TraceError(Exception exception, string messageFormat, object[] messageArgs)
        {
            string traceFormat;
            object[] traceArgs;
            int traceId;

            if (exception != null)
            {
                traceFormat = string.Concat("[{", messageArgs.Length,
                    ":s}] - ",
                    messageFormat,
                    '{', messageArgs.Length + 1, '}',
                    '{', messageArgs.Length + 2, '}');

                traceArgs = new object[messageArgs.Length + 3];
                messageArgs.CopyTo(traceArgs, 0);
                traceArgs[traceArgs.Length - 3] = DateTime.Now;
                traceArgs[traceArgs.Length - 2] = Environment.NewLine;
                traceArgs[traceArgs.Length - 1] = exception;

                traceId = exception.HResult;
            }
            else
            {
                traceFormat = string.Concat("[{", messageArgs.Length,
                    ":s}] - ",
                    messageFormat);

                traceArgs = new object[messageArgs.Length + 1];
                messageArgs.CopyTo(traceArgs, 0);
                traceArgs[traceArgs.Length - 3] = DateTime.Now;

                traceId = 0;
            }

            TraceSource.TraceEvent(TraceEventType.Error, traceId, traceFormat, traceArgs);
        }
    }
}
