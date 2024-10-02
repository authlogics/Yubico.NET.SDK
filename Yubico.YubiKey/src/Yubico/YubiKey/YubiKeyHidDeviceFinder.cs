
#pragma warning disable IDE0011 // Add braces
#pragma warning disable CA1031 // Do not catch general exception types

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Yubico.Core.Devices;
using Yubico.Core.Devices.Hid;
using Yubico.Core.Devices.SmartCard;
using Yubico.Core.Logging;
using Yubico.PlatformInterop;
using Yubico.YubiKey.DeviceExtensions;

namespace Yubico.YubiKey
{
    /// <summary>
    /// This class is based on the YubiKeyDeviceListener, but now serves only to find currently available HID FIDO devices
    /// </summary>
    public class YubiKeyHidDeviceFinder : IDisposable
    {

        /// <summary>
        /// An instance of a <see cref="YubiKeyHidDeviceFinder"/>.
        /// </summary>
        public static YubiKeyHidDeviceFinder Instance => _lazyInstance.Value;

        private static readonly Lazy<YubiKeyHidDeviceFinder> _lazyInstance = new Lazy<YubiKeyHidDeviceFinder>(() => new YubiKeyHidDeviceFinder());

        private static readonly ReaderWriterLockSlim RwLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        private readonly Logger _log = Log.GetLogger();
        private readonly Dictionary<IYubiKeyDevice, bool> _internalCache = new Dictionary<IYubiKeyDevice, bool>();

        internal List<IYubiKeyDevice> GetAll()
        {
            try
            {
                //Since we no longer have a background thread for device arrival, we shouldnt need to use a lock, but we will keep the code
                //in case this changes in the future. Since the same key may be present on multiple transports, we keep the complex merge logic in place too
                RwLock.EnterWriteLock();
                _log.LogInformation("Entering write-lock.");

                //We do a full cache clear and update each time, because we no longer listen for device arrival and removal
                _internalCache.Clear();
                Update();

                RwLock.ExitWriteLock();
            }
            catch (Exception ex)
            {
                _log.LogError("An error occurred while updating the cache: {Error}", ex);
                if (RwLock.IsWriteLockHeld) RwLock.ExitWriteLock();
            }
            
            return _internalCache.Keys.ToList();
        }

        private void Update()
        {
            ResetCacheMarkers();

            List<IDevice> devicesToProcess = GetDevices();

            _log.LogInformation("Cache currently aware of {Count} YubiKeys.", _internalCache.Count);

            var addedYubiKeys = new List<IYubiKeyDevice>();

            foreach (IDevice device in devicesToProcess)
            {
                _log.LogInformation("Processing device {Device}", device);

                // First check if we've already seen this device (very fast)
                IYubiKeyDevice? existingEntry = _internalCache.Keys.FirstOrDefault(k => k.Contains(device));

                if (existingEntry != null)
                {
                    MarkExistingYubiKey(existingEntry);
                    continue;
                }

                // Next, see if the device has any information about its parent, and if we can match that way (fast)
                existingEntry = _internalCache.Keys.FirstOrDefault(k => k.HasSameParentDevice(device));

                if (existingEntry is YubiKeyDevice parentDevice)
                {
                    MergeAndMarkExistingYubiKey(parentDevice, device);
                    continue;
                }

                // Lastly, let's talk to the YubiKey to get its device info and see if we match via serial number (slow)
                YubiKeyDevice.YubicoDeviceWithInfo deviceWithInfo;

                // This sort of call can fail for a number of reasons. Probably the most common will be when some other
                // application is using one of the device interfaces exclusively - GPG is an example of this. It tends
                // to take the smart card reader USB interface and not let go of it. So, for those of us that use GPG
                // with YubiKeys for commit signing, the SDK is unlikely to be able to connect. There's not much we can
                // do about that other than skip, and log a message that this has happened.
                try
                {
                    deviceWithInfo = new YubiKeyDevice.YubicoDeviceWithInfo(device);
                }
                catch (Exception ex) when (ex is SCardException || ex is PlatformApiException)
                {
                    _log.LogError("Encountered a YubiKey but was unable to connect to it. This interface will be ignored.");

                    continue;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException)
                {
                    _log.LogError($"Encountered a YubiKey but was unable to connect to it. {ex}");

                    continue;
                }

                if (deviceWithInfo.Info.SerialNumber is null)
                {
                    CreateAndMarkNewYubiKey(deviceWithInfo, addedYubiKeys);

                    continue;
                }

                existingEntry = _internalCache.Keys.FirstOrDefault(k => k.SerialNumber == deviceWithInfo.Info.SerialNumber);

                if (existingEntry is YubiKeyDevice mergeTarget)
                {
                    MergeAndMarkExistingYubiKey(mergeTarget, deviceWithInfo);

                    continue;
                }

                CreateAndMarkNewYubiKey(deviceWithInfo, addedYubiKeys);
            }
        }

        private List<IDevice> GetDevices()
        {
            var devicesToProcess = new List<IDevice>();

            IList<IDevice> hidFidoDevices = new List<IDevice>();

            if (SdkPlatformInfo.OperatingSystem == SdkPlatform.Windows && !SdkPlatformInfo.IsElevated)
            {
                _log.LogWarning("SDK running in an un-elevated Windows process. Skipping FIDO enumeration as this requires process elevation.");
            }
            else
            {
                hidFidoDevices = GetHidFidoDevices().ToList();
            }

            _log.LogInformation("Found {FidoCount} HID FIDO devices for processing.", hidFidoDevices.Count);

            devicesToProcess.AddRange(hidFidoDevices);

            return devicesToProcess;
        }

        private void ResetCacheMarkers()
        {
            // Copy the list of keys as changing a dictionary's value will invalidate any enumerators (i.e. the loop).
            foreach (IYubiKeyDevice cacheDevice in _internalCache.Keys.ToList())
            {
                _internalCache[cacheDevice] = false;
            }
        }

        private void MergeAndMarkExistingYubiKey(YubiKeyDevice mergeTarget, YubiKeyDevice.YubicoDeviceWithInfo deviceWithInfo)
        {
            _log.LogInformation(
                "Device was not found in the cache, but appears to be YubiKey {Serial}. Merging devices.",
                mergeTarget.SerialNumber);

            mergeTarget.Merge(deviceWithInfo.Device, deviceWithInfo.Info);
            _internalCache[mergeTarget] = true;
        }

        private void MergeAndMarkExistingYubiKey(YubiKeyDevice mergeTarget, IDevice newChildDevice)
        {
            _log.LogInformation(
                "Device was not found in the cache, but appears to share the same composite device as YubiKey {Serial}."
                + " Merging devices.",
                mergeTarget.SerialNumber);

            mergeTarget.Merge(newChildDevice);
            _internalCache[mergeTarget] = true;
        }

        private void MarkExistingYubiKey(IYubiKeyDevice existingEntry)
        {
            _log.LogInformation(
                "Device was found in the cache and appears to be YubiKey {Serial}.",
                existingEntry.SerialNumber);

            _internalCache[existingEntry] = true;
        }

        private void CreateAndMarkNewYubiKey(YubiKeyDevice.YubicoDeviceWithInfo deviceWithInfo, List<IYubiKeyDevice> addedYubiKeys)
        {
            _log.LogInformation(
                "Device appears to be a brand new YubiKey with serial {Serial}",
                deviceWithInfo.Info.SerialNumber
                );

            var newYubiKey = new YubiKeyDevice(deviceWithInfo.Device, deviceWithInfo.Info);
            addedYubiKeys.Add(newYubiKey);
            _internalCache[newYubiKey] = true;
        }

        private static IEnumerable<IDevice> GetHidFidoDevices()
        {
            try
            {
                return HidDevice
                    .GetHidDevices()
                    .Where(d => d.IsFido());
            }
            catch (PlatformInterop.PlatformApiException e) { ErrorHandler(e); }

            return Enumerable.Empty<IDevice>();
        }

        private static void ErrorHandler(Exception exception) =>
            Log.GetLogger().LogWarning($"Exception caught: {exception}");

        #region IDisposable Support

        private bool _disposedValue;

        /// <summary>
        /// Disposes the objects.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    RwLock.Dispose();
                }
                _disposedValue = true;
            }
        }

        ~YubiKeyHidDeviceFinder()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        /// <summary>
        /// Calls Dispose(true).
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
