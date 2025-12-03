// Copyright 2025 Yubico AB
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

using System;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Yubico.Core.Logging
{
    public static partial class Log
    {
        private static ILoggerFactory? _factory;

        [Obsolete("Obsolete, use Log.Instance instead. Setting this will override the default dotnet console logger.")]
        public static ILoggerFactory LoggerFactory
        {
            get => _factory ??= new NullLoggerFactory();
            set
            {
                _factory = value;

                // Also swap out the new implementation instance
                Instance = value;
            }
        }

        /// <summary>
        /// Gets an instance of the active logger, bypassing the factory
        /// </summary>
        [Obsolete("Obsolete, use equivalent ILogger method, or view the changelog for further instruction.")]
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
                        var logFileNameOutput = string.Format(CultureInfo.InvariantCulture, name, $"{now.Year}{now.Month}{now.Day}{now.Hour}{now.Minute}{now.Second}");

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
                if (!string.IsNullOrEmpty(folder)) { _loggingFolder = folder; }

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
