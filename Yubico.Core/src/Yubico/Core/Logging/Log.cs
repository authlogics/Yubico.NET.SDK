// Copyright 2021 Yubico AB
//
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#pragma warning disable IDE0008 // Use explicit type
#pragma warning disable IDE0011 // Add braces
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA1305 // Specify IFormatProvider
#pragma warning disable CA1810 // Initialize reference type static fields inline

using System.Collections.Generic;
using System.Text;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Net.WebSockets;

namespace Yubico.Core.Logging
{
    /// <summary>
    /// A static class for managing Yubico SDK logging for this process.
    /// </summary>
    public static class Log
    {
        private static ILoggerFactory? _factory;

        private static Dictionary<string, Logfile> _logs;
        private static readonly object _lock;
        private static string _loggingFolder;
        private static bool _loggingEnabled;

        static Log()
        {
            _logs = new Dictionary<string, Logfile>();
            _lock = new object();
            _loggingFolder = "";
            _loggingEnabled = false;

            GetProcessSettings();
        }

        /// <summary>
        /// The logger factory implementation that should be used by the SDK. Use this to set the active logger.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The LoggerFactory controls how the concrete log(s) that the SDK will use get created. This is something that
        /// should be controlled by the application using the SDK, and not the SDK itself. The application can decide
        /// whether they would like to send events to the Windows Event Log, or to a cross platform logger such as NLog,
        /// Serilog, or others. An application can decide to send log messages to multiple sinks as well (see examples).
        /// </para>
        /// <para>
        /// The <see cref="ILoggerFactory"/> interface is the same one that is used by `Microsoft.Extensions.Logging.` You
        /// can read more about how to integrate with this interface in the
        /// [Logging in .NET](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging) webpage provided by Microsoft.
        /// </para>
        /// </remarks>
        /// <example>
        /// <para>
        /// Send SDK log messages to the console:
        /// </para>
        /// <code language="csharp">
        /// using Microsoft.Extensions.Logging;
        /// using Yubico.Core.Logging;
        ///
        /// static class Program
        /// {
        ///     static void EnableLogging()
        ///     {
        ///         Log.LoggerFactory = LoggerFactory.Create(
        ///             builder => builder.AddSimpleConsole(
        ///                options =>
        ///                {
        ///                    options.IncludeScopes = true;
        ///                    options.SingleLine = true;
        ///                    options.TimestampFormat = "hh:mm:ss";
        ///                })
        ///                .AddFilter(level => level >= LogLevel.Information));
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <example>
        /// <para>
        /// Send SDK log messages to Serilog.
        /// </para>
        /// <para>
        /// First, begin by adding a package reference to `Serilog.Extensions.Logging` and `Serilog.Sinks.Console` (or
        /// to the appropriate sink you plan to use).
        /// </para>
        /// <para>
        /// Now, you can add the following code to your application:
        /// </para>
        /// <code language="csharp">
        /// using Microsoft.Extensions.Logging;
        /// using Serilog;
        /// using Yubico.Core.Logging;
        ///
        /// static class Program
        /// {
        ///     static void EnableLogging()
        ///     {
        ///         // Serilog does setup through its own LoggerConfiguration builder. The factory will
        ///         // pick up the log from Serilog.Log.Logger.
        ///         Serilog.Log.Logger = new LoggerConfiguration()
        ///             .Enrich().FromLogContext()
        ///             .WriteTo.Console()
        ///             .CreateLogger();
        ///
        ///         // Fully qualified name to avoid conflicts with Serilog types
        ///         Yubico.Core.Logging.Log.LoggerFactory = LoggerFactory.Create(
        ///             builder => builder
        ///                .AddSerilog(dispose: true)
        ///                .AddFilter(level => level >= LogLevel.Information));
        ///     }
        /// }
        /// </code>
        /// </example>
        public static ILoggerFactory LoggerFactory
        {
            get => _factory ??= new NullLoggerFactory();
            set => _factory = value;
        }

        /// <summary>
        /// Gets an instance of the active logger, bypassing the factory
        /// </summary>
        public static Logger GetLogger()
        {   
            //Return a logger working in two different ways, depending on whether a LoggerFactory has been set
            if (!(_factory == null || _factory is NullLoggerFactory))
            {
                return new Logger(LoggerFactory.CreateLogger("Yubico.Core logger"));
            }
            return new Logger("WDAYubiKey-{0}");
        }

        /// <summary>
        /// Gets an instance of the underlying logfile object
        /// </summary>
        public static Logfile GetLogFile(string name, bool overwrite)
        {
            // Ensure the logs collection and log file is created once
            lock (_lock)
            {
                // First time we are getting a logfile
                if (_logs.Count == 0)
                {
                    // In .net core, we need to register the additional code pages we use here
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                }

                // If logfile with this product name and unformatted filename is not found, then create it and add it to the collection of logs
                if (!_logs.TryGetValue(name, out var logfile))
                {
                    // If we havent managed to get a valid value, create a blank non-logging logfile object instead
                    if (string.IsNullOrEmpty(_loggingFolder))
                    {
                        logfile = new Logfile();
                    }
                    else
                    {
                        // Determine the logfile name
                        // Format e.g. AuthlogicsAuthenticationServerManager-{0}.log
                        //Name should contain the location for the date string in parameter 0 ie {0}
                        var now = DateTime.Now;
                        var logFileNameOutput = string.Format(name, $"{now.Year}{now.Month}{now.Day}{now.Hour}{now.Minute}{now.Second}");

                        logfile = new Logfile(logFileNameOutput, _loggingFolder, _loggingEnabled, overwrite)
                        {
                            SafeLog = true
                        };
                    }

                    // Write out the version so we know what version of the Authlogics.dll we have on the system
                    logfile.AddVersion();

                    _logs.Add(name, logfile);
                }

                return logfile;
            }
        }

        private static void GetProcessSettings()
        {
            var path = "SOFTWARE\\Authlogics\\Windows Desktop Agent\\";
            var registry = new Registry(path);

            try
            {
                var folder = registry.GetValue("LoggingFolder", RegistryValueKind.String).ToString();
                if (!string.IsNullOrEmpty(folder)) _loggingFolder = folder;

                var value = registry.GetValue("LoggingEnabled", RegistryValueKind.DWord, false);
                if (value.ToString() == "1")
                {
                    _loggingEnabled = true;
                }
            }
            catch (Exception)
            {
                //Cant log anything here as we are in the logging class
            }
        }
    }
}
