#pragma warning disable IDE0008 // Use explicit type
#pragma warning disable IDE0011 // Add braces
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA1307 // Specify StringComparison
#pragma warning disable CA1720 // Identifier contains type name

using System;
using Microsoft.Win32;

namespace Yubico.Core.Logging
{
    // Map the registry value kinds so that the entire registry dll does not need to be shared with other libraries
    public enum RegistryValueKind
    {
        None = -1,
        Unknown = 0,
        String = 1,
        ExpandString = 2,
        Binary = 3,
        DWord = 4,
        MultiString = 7,
        QWord = 11
    }

    public class Registry
    {
        private const string DefaultAuthlogicsRegistryKeyPath = @"SOFTWARE\Authlogics\Authentication Server";
        private const RegistryHive DefaultRegistryHive = RegistryHive.LocalMachine;
        private const RegistryView DefaultServerRegistryView = RegistryView.Registry64;

        private RegistryView _registryView;

        public string KeyPath { get; private set; }
        public bool KeyPathExists { get; private set; }
        public RegistryHive RegistryHive { get; private set; }

        /// <summary>
        /// Create a new registry helper object for the Authentication Server registry key.
        /// </summary>
        /// <remarks></remarks>
        public Registry()
        {
            RegistryHive = DefaultRegistryHive;
            _registryView = DefaultServerRegistryView;
            KeyPath = DefaultAuthlogicsRegistryKeyPath;

            CheckKeyPathExists();
        }

        /// <summary>
        /// Create a new registry helper object for the key path provided.
        /// </summary>
        /// <param name="registryKeyPath">Full registry path without the registry hive, e.g. SOFTWARE\Authlogics\Authentication Server</param>
        /// <remarks></remarks>
        public Registry(string registryKeyPath)
        {
            if (string.IsNullOrEmpty(registryKeyPath))
            {
                RegistryHive = DefaultRegistryHive;
                _registryView = DefaultServerRegistryView;
                KeyPath = DefaultAuthlogicsRegistryKeyPath;
            }
            else
            {
                RegistryHive = DefaultRegistryHive;
                _registryView = RegistryView.Default;
                KeyPath = registryKeyPath.Replace(@"HKEY_LOCAL_MACHINE\", "");
            }

            CheckKeyPathExists();
        }

        private void CheckKeyPathExists()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive, _registryView))
                using (var key = hklm.OpenSubKey(KeyPath))
                {
                    KeyPathExists = key != null;
                }
            }
            catch (Exception)
            {
                KeyPathExists = false;
            }
        }

        /// <summary>
        /// Create a new registry helper object for the key path and settings provided.
        /// </summary>
        /// <param name="registryKeyPath">Full registry path without the registry hive, e.g. SOFTWARE\Authlogics\Authentication Server</param>
        /// <param name="registryHive">The registry hive, e.g. HKEY_LOCAL_MACHINE</param>
        /// <param name="registryView">The registry view, e.g. RegistryView.Registry64</param>
        /// <remarks></remarks>
        public Registry(string registryKeyPath, RegistryHive registryHive, RegistryView registryView)
        {
            RegistryHive = registryHive;
            _registryView = registryView;
            KeyPath = registryKeyPath;
        }

        /// <summary>
        /// Reads a registry value from the current key.
        /// </summary>
        /// <returns>Returns the value data in the format requested.</returns>
        public object GetValue(string valueName, RegistryValueKind valueType, object? defaultValue = null)
        {

            if (defaultValue == null)
            {
                // Set default value
                switch (valueType)
                {
                    case RegistryValueKind.Binary:
                    case RegistryValueKind.DWord:
                    case RegistryValueKind.Unknown:
                        {
                            defaultValue = 0;
                            break;
                        }

                    default:
                        {
                            defaultValue = "";
                            break;
                        }
                }
            }

            // Dim result = Microsoft.Win32.Registry.GetValue(_keyPath, valueName, defaultValue)
            object? result = null;

            // https://stackoverflow.com/questions/13728491/opensubkey-returns-null-for-a-registry-key-that-i-can-see-in-regedit-exe
            // https://social.msdn.microsoft.com/Forums/vstudio/en-US/e206cebd-28fe-4c38-a4fb-a214f65ce56f/64-bit-version-of-iis-express?forum=visualstudiogeneral
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive, _registryView))
            using (var key = hklm.OpenSubKey(KeyPath))
            {
                if (key is null)
                {
                    throw new ApplicationException($"Registry key \"{KeyPath}\" could not be found. Registry view: {_registryView}. Is 64bit process: {Environment.Is64BitProcess}.");
                }
                else
                {
                    result = key.GetValue(valueName, defaultValue);
                }
            }

            if (result is null)
            {
                throw new ApplicationException($@"Registry value ""{KeyPath}\{valueName}"" could not be found. Registry view: {_registryView}. Is 64bit process: {Environment.Is64BitProcess}.");
            }

            return result;
        }

        /// <summary>
        /// Writes a registry value to the current key.
        /// </summary>
        /// <param name="valueName">Name of the value.</param>
        /// <param name="valueData">Data to be stored in the value.</param>
        public void SetValue(string valueName, object valueData)
        {
            // Microsoft.Win32.Registry.SetValue(_keyPath, valueName, valueData)

            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive, _registryView))
            using (var key = hklm.OpenSubKey(KeyPath, true))
            {
                if (key != null) key.SetValue(valueName, valueData);
            }
        }

        /// <summary>
        /// Deletes a registry value from the key.
        /// </summary>
        /// <param name="valueName">Name of the value.</param>
        public void DeleteValue(string valueName)
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive, _registryView))
            using (var key = hklm.OpenSubKey(KeyPath, true))
            {
                if (key != null) key.DeleteValue(valueName);
            }
        }
    }
}
