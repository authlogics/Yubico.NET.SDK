#pragma warning disable IDE0008 // Use explicit type
#pragma warning disable IDE0011 // Add braces
#pragma warning disable IDE0032 // Use auto property
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable IDE0060 // Remove unused parameter
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA1304 // Specify CultureInfo
#pragma warning disable CA1305 // Specify IFormatProvider
#pragma warning disable CA1307 // Specify StringComparison
#pragma warning disable CA1310 // Specify StringComparison for correctness
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;



namespace Yubico.Core.Logging
{
    public sealed class Logfile : IDisposable
    {

        private System.IO.StreamWriter _logStream;
        private string _logFilePath;
        private bool _append;
        private bool _showDateTime;
        private bool _showThreadID;
        private bool _showMethod;
        private int _boxWidth;
        private DateTime _startTime;
        private DateTime _lastAccessTime;
        private bool _safeLog;
        private bool _enabled;
        private bool _autoFlush;
        private bool _setupLogStream;

        // Much faster to work these out just once
        private static string _unicode186 = ChrDOStoUnicode(186);
        private static string _unicode187 = ChrDOStoUnicode(187);
        private static string _unicode188 = ChrDOStoUnicode(188);
        private static string _unicode196 = ChrDOStoUnicode(196);
        private static string _unicode200 = ChrDOStoUnicode(200);
        private static string _unicode201 = ChrDOStoUnicode(201);
        private static string _unicode205 = ChrDOStoUnicode(205);

        private static object _lock = new object();

        public Logfile()
        {
            _enabled = false;
        }

        /// <summary>
    /// Creates a new logfile instance with a custom registry key location
    /// This is used by agents e.g. Windows Desktop Logon Agent, which uses this logging class but has a different registry location.
    /// </summary>
        public Logfile(string logFileName, string loggingFolder, bool enabled, bool overwrite = false)
        {
            if (string.IsNullOrEmpty(logFileName)) throw new ArgumentNullException(nameof(logFileName));
            if (string.IsNullOrEmpty(loggingFolder)) throw new ArgumentNullException(nameof(loggingFolder), "Logging Folder cannot be null or empty. Use new Logfile() instead.");

            var logPath = loggingFolder;

            // Don't lookup the values from the registry, use the settings provided
            _enabled = enabled;

            // Make sure there is a slash on the end of the folder path
            if (Right(logPath, 1) != @"\") logPath = string.Concat(logPath, @"\");

            // Make sure there is a .log on the end
            if (!logFileName.Contains(".log")) logFileName = string.Concat(logFileName, ".log");

            // Add the path and file name together
            _logFilePath = string.Concat(logPath, logFileName);

            if (!System.IO.Directory.GetParent(_logFilePath).Exists) throw new Exception($"The directory for the specified log file can not be found: {System.IO.Directory.GetParent(_logFilePath)}");

            _startTime = DateTime.Now;
            _lastAccessTime = DateTime.Now;
            _showDateTime = true;
            _showThreadID = true;
            _showMethod = true;
            _safeLog = true;

            // Opposite of Overwrite
            _append = !overwrite;

            // Sets the width of the boxes. 84 seems to be the best width for when logs are printed.
            _boxWidth = 84;
        }

        /// <summary>
        /// Parses a password and returns *'s to replace letters, unless a ClearTextPasswordEnabled registry key is enabled or the password is numeric only then the clear text is returned. 
        /// </summary>
        /// <param name="password">Password to process</param>
        /// <param name="appRegKey">Optional reg path section of agent to look for key in, e.g. 'Windows Desktop Agent'</param>
        /// <returns>****** or clear text password</returns>
        public static string FormatPassword(string password, string appRegKey = "")
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            if (ClearTextPasswordEnabled(appRegKey)) return password;

            // Calculate the length of the MIP then build a string of stars matching the length
            return new string('*', password.Length);
        }

        private static bool ClearTextPasswordEnabled(string appRegKey = "")
        {
            return false;
        }

        public void Dispose()
        {
            Close();
        }

        public int BoxWidth
        {
            get => _boxWidth;
            set => _boxWidth = value;
        }

        public string LogFilePath => _logFilePath;

        public bool ShowDateTime
        {
            get => _showDateTime;
            set => _showDateTime = value;
        }

        public bool ShowThreadID
        {
            get => _showThreadID;
            set => _showThreadID = value;
        }

        public bool ShowMethod
        {
            get => _showMethod;
            set => _showMethod = value;
        }

        public DateTime LogStart => _startTime;

        public DateTime LogLastAccess => _lastAccessTime;

        /// <summary>
        /// Safe Log closes and reopens the log file between each entry being written. This will have a performance impact.
        /// </summary>
        public bool SafeLog
        {
            get => _safeLog;
            set => _safeLog = value;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Automatically flushed the log after each log entry.
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        /// <remarks></remarks>
        public bool AutoFlush
        {
            get => _autoFlush;
            set
            {
                _autoFlush = value;

                // Update the actual stream if it has been created
                lock (_lock)
                {
                    if (_logStream != null) _logStream.AutoFlush = _autoFlush;
                }
            }
        }

        public TimeSpan GetRunTime()
        {
            return DateTime.Now - _startTime;
        }

        public string GetRunTimeString()
        {
            var timeTaken = GetRunTime();
            var temp = ((int)Math.Round(timeTaken.TotalSeconds) % 60) + " Seconds.";

            if ((int) Math.Round(timeTaken.TotalMinutes) % 60 > 0) temp = ((int) Math.Round(timeTaken.TotalMinutes) % 60) + " Minutes and " + temp;
            if ((int) Math.Round(timeTaken.TotalHours) % 24 > 0) temp = ((int) Math.Round(timeTaken.TotalHours) % 24) + " Hours, " + temp;
            if ((int) Math.Round(timeTaken.TotalDays) > 0) temp = (int) Math.Round(timeTaken.TotalDays) + " Days, " + temp;

            return temp;
        }

        public void ClearLog()
        {
            if (!_enabled) return;

            lock (_lock)
            {
                // Close down the current stream object
                _logStream.Close();
                _logStream = null;

                // Set the append state to false so it gets overridden
                _append = false;

                // Setup the new log object
                SetupLogStream();
            }
        }

        public void Flush()
        {
            if (!_enabled) return;

            lock (_lock)
            {
                _logStream.Flush();
            }
        }

        public void Close()
        {
            if (!_enabled) return;

            // Safe logging always closes the file so this sub is not required
            lock (_lock)
            {
                if (!_safeLog)
                {
                    try
                    {
                        _logStream.Flush();
                        _logStream.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public void AddText(string line, bool autoNewLine = true, [CallerMemberName] string memberName = "")
        {
            if (!_enabled) return;

            var caller = "";

            if (_showMethod)
            {
                var stackTrace = new StackTrace();

                var frameIndex = 1;
                var method = stackTrace.GetFrame(frameIndex).GetMethod();
                var typeName = (method.DeclaringType?.Name) ?? string.Empty;

                // If the type is Logfile then we want to go up a frame to get to the actual type
                while ((typeName ?? "") == nameof(Logfile))
                {
                    frameIndex += 1;
                    method = stackTrace.GetFrame(frameIndex).GetMethod();
                    typeName = (method.DeclaringType?.Name) ?? string.Empty;
                }

                // If the method is a constructor then the methodName will be .ctor, a .. in the log looks a bit funny
                if (method.Name.StartsWith("."))
                {
                    caller = $"[{typeName}{method.Name}]";
                }
                else
                {
                    caller = $"[{typeName}.{method.Name}]";
                }

                // Detect Async continuation
                if (method.Name.EndsWith("MoveNext"))
                {
                    if (typeName.Contains("StateMachine"))
                    {
                        caller = $"[{memberName}]";
                    }
                    else
                    {
                        caller = $"[{typeName}.{memberName}]";
                    }
                }
            }

            if (_showDateTime)
            {
                var now = DateTime.Now;

                if (_showThreadID)
                {
                    LogLine($"{caller} {now.ToShortDateString()} {now.ToLongTimeString()} - T{Environment.CurrentManagedThreadId:D3} - {line}", autoNewLine);
                }
                else
                {
                    LogLine($"{caller} {now.ToShortDateString()} {now.ToLongTimeString()} - {line}", autoNewLine);
                }
            }
            else if (_showThreadID)
            {
                LogLine($"{caller} {Environment.CurrentManagedThreadId:D3} - {line}", autoNewLine);
            }
            else
            {
                LogLine($"{caller} {line}", autoNewLine);
            }
        }

        /// <summary>
        /// Adds the log entry and substitues the Length property of the provided string while also checking for Nothing
        /// </summary>
        /// <param name="line"></param>
        /// <param name="value"></param>
        /// <param name="autoNewLine"></param>
        public void AddTextLength(string line, string value, bool autoNewLine = true)
        {
            if (!_enabled) return;

            var length = (value == null) ? "Nothing" : value.Length.ToString();

            AddText(string.Format(line, length), autoNewLine);
        }

        /// <summary>
        /// Adds the log entry writing out each key value pair
        /// </summary>
        /// <param name="keyValuePairArray"></param>
        public void AddKeyValuePairs(KeyValuePair<string, string>[] keyValuePairArray)
        {
            if (keyValuePairArray == null) throw new ArgumentNullException(nameof(keyValuePairArray));

            if (!_enabled) return;

            if (keyValuePairArray.Length == 0) AddText("No items were found");

            foreach (var kvp in keyValuePairArray)
            {
                if (kvp.Key.ToLower().Contains("password") || kvp.Key.ToLower().Contains("passcode") || kvp.Key.ToLower().Contains("plaintextpassword"))
                {
                    AddText("Key pair: " + kvp.Key + " = " + FormatPassword(kvp.Value ?? ""));
                }
                else
                {
                    AddText("Key pair: " + kvp.Key + " = " + (kvp.Value ?? ""));
                }
            }
        }

        /// <summary>
        /// Adds the log entry and substitues the Length property of the provided array while also checking for Nothing
        /// </summary>
        public void AddText(string line, Array value, bool autoNewLine = true, [CallerMemberName] string caller = "")
        {
            if (!_enabled) return;
            var length = (value == null) ? "Nothing": value.Length.ToString(); 

            AddText(string.Format(line, length), autoNewLine, caller);
        }

        public void AddVersion()
        {
            if (!_enabled) return;

            // Get the version from the assembly
            var verInfo = $"Assembly Info: {System.Reflection.Assembly.GetExecutingAssembly().GetName().FullName}";

            AddText(verInfo, true);
        }

        public void AddLineBlank()
        {
            if (!_enabled) return;
            LogLine("");
        }

        public void AddLineSingle()
        {
            if (!_enabled) return;
            LogLine(GetLineSingle());
        }

        public void AddLineDouble()
        {
            if (!_enabled) return;
            LogLine(GetLineDouble());
        }

        /// <summary>
        /// Adds the message text into a double boardered banner box.
        /// Supports multi-line strings and auto text wraps to the maximum box width.
        /// </summary>
        /// <param name="message">Message to display in header.</param>
        /// <remarks></remarks>
        public void AddHeader1([CallerMemberName] string message = null)
        {
            if (!_enabled) return;

            var lines = new List<string>
            {
                "",
                GetBoxDoubleTop()
            };

            // Add thread number to heading
            message = $"T{Environment.CurrentManagedThreadId.ToString("D3")} - {message}";

            var split = message.Split('\n');

            // Process each line
            foreach (var line in split)
            {
                var tempString = line.Trim();

                // Make sure each line will fit in the box, if not line wrap it.
                if (line.Length > _boxWidth - 4)
                {
                    var loops = line.Length / (_boxWidth - 4);
                    for (int i = 1, loopTo = loops; i <= loopTo; i++)
                    {
                        lines.Add(GetBoxDoubleSides(tempString.Substring(0, _boxWidth - 4)));
                        tempString = tempString.Remove(0, _boxWidth - 4);
                    }

                    // Write out what is left over.
                    lines.Add(GetBoxDoubleSides(tempString));
                }
                else
                {
                    lines.Add(GetBoxDoubleSides(tempString));
                }
            }

            lines.Add(GetBoxDoubleBottom());

            LogLines(lines.ToArray());
        }

        /// <summary>
        /// Adds the message text between a double line on the top and a single line at the bottom.
        /// </summary>
        /// <param name="message">Message to display in header.</param>
        /// <remarks></remarks>
        public void AddHeader2([CallerMemberName] string message = null)
        {
            if (!_enabled) return;

            var lines = new List<string>
            {
                "",
                GetLineSingle(),
                $"  T{Environment.CurrentManagedThreadId.ToString("D3")} - {message}",
                GetLineSingle()
            };

            LogLines(lines.ToArray());
        }

        /// <summary>
        /// Directly adds text entry does NOT add a date stamp or perform formatting.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="autoNewLine"></param>
        public void LogLine(string line, bool autoNewLine = true)
        {
            lock (_lock)
            {
                try
                {
                    // Setup the new log object
                    if (!_setupLogStream) SetupLogStream();

                    if (_safeLog)
                    {
                        _logStream.Close();

                        // Reopen the log file with the same settings as before
                        _logStream = new System.IO.StreamWriter(_logFilePath, true, Encoding.Unicode)
                        {
                            AutoFlush = _autoFlush
                        };
                        _lastAccessTime = DateTime.Now;
                    }

                    if (autoNewLine)
                    {
                        _logStream.WriteLine(line);
                    }
                    else
                    {
                        _logStream.Write(line);
                    }

                    if (_safeLog)
                    {
                        // Close the actual log file
                        _logStream.Flush();
                        _logStream.Close();
                    }
                }
                catch (Exception)
                {
                    // We have no where to log the exception to, so we have to ignore it 
                }

                _lastAccessTime = DateTime.Now;
            }
        }

        /// <summary>
        /// Directly adds text entry does NOT add a date stamp or perform formatting.
        /// </summary>
        /// <param name="lines"></param>
        /// <param name="autoNewLine"></param>
        public void LogLines(IEnumerable<string> lines, bool autoNewLine = true)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));

            lock (_lock)
            {
                try
                {
                    // Setup the new log object
                    if (!_setupLogStream) SetupLogStream();

                    if (_safeLog)
                    {
                        _logStream.Close();

                        // Reopen the log file with the same settings as before
                        _logStream = new System.IO.StreamWriter(_logFilePath, true, Encoding.Unicode)
                        {
                            AutoFlush = _autoFlush
                        };
                        _lastAccessTime = DateTime.Now;
                    }

                    foreach (var line in lines)
                    {
                        if (autoNewLine)
                        {
                            _logStream.WriteLine(line);
                        }
                        else
                        {
                            _logStream.Write(line);
                        }
                    }

                    if (_safeLog)
                    {
                        // Close the actual log file
                        _logStream.Flush();
                        _logStream.Close();
                    }
                }
                catch (Exception)
                {
                    // We have no where to log the exception to, so we have to ignore it 
                }

                _lastAccessTime = DateTime.Now;
            }
        }

        // This should be called inside a sync lock
        private void SetupLogStream()
        {
            lock (_lock)
            {
                // Create an instance of StreamWriter to write logging info to a file.
                _logStream = new System.IO.StreamWriter(_logFilePath, _append, Encoding.Unicode)
                {
                    AutoFlush = _autoFlush
                };
                _lastAccessTime = DateTime.Now;
                _setupLogStream = true;
            }
        }

        private string GetLineSingle()
        {
            var strTemp = string.Empty;

            for (int i = 1, loopTo = _boxWidth; i <= loopTo; i++) strTemp = string.Concat(strTemp, _unicode196);
            return strTemp;
        }

        private string GetLineDouble()
        {
            var strTemp = string.Empty;

            // Horizontal double line
            for (int i = 1, loopTo = _boxWidth; i <= loopTo; i++) strTemp = string.Concat(strTemp, _unicode205);

            return strTemp;
        }

        private static string ChrDOStoUnicode(int ChrNumber)
        {
            // Converts a DOS 850 character set Char number to a unicode Char
            // Create byte array of a  single unit
            var bytDOS850 = new byte[1];

            // Set the encoding source type to codepage 850 - DOS
            var encSource = Encoding.GetEncoding(850);

            try
            {
                // Convert the provided character number to a byte value and place in first array position
                bytDOS850[0] = (byte)ChrNumber;

                // Perform the conversion from one encoding to the other.
                var bytUnicode = Encoding.Convert(encSource, Encoding.Unicode, bytDOS850);

                // Output the byte array to a string
                return new string(Encoding.Unicode.GetChars(bytUnicode));
            }

            catch (Exception)
            {
                return "";
            }
        }

        private string GetBoxDoubleTop()
        {
            // Top left corner
            var strTemp = _unicode201;

            // Horizontal line less first and last position
            for (int i = 2, loopTo = _boxWidth - 1; i <= loopTo; i++) strTemp = string.Concat(strTemp, _unicode205);

            // Top left corner
            return string.Concat(strTemp, _unicode187);
        }

        private string GetBoxDoubleBottom()
        {
            // Top left corner
            var strTemp = _unicode200;

            // Horizontal line less first and last position
            for (int i = 2, loopTo = _boxWidth - 1; i <= loopTo; i++) strTemp = string.Concat(strTemp, _unicode205);

            // Top left corner
            return string.Concat(strTemp, _unicode188);
        }

        private string GetBoxDoubleSides(string message = "")
        {
            // Prepend the provided message into the line
            var strTemp = string.Concat(_unicode186, " ", message);

            // Fill the line with spaces
            for (int i = 1, loopTo = _boxWidth - message.Length - 3; i <= loopTo; i++)
                strTemp = string.Concat(strTemp, " ");

            return string.Concat(strTemp, _unicode186);
        }

        private static string Right(string str, int Length)
        {
            if (Length < 0) throw new ArgumentException("Argument 'Length' must be greater or equal to zero");

            if (str is null || str.Length == 0) return string.Empty;

            if (Length >= str.Length) return str;

            return str.Substring(str.Length - Length);
        }
    }
}
