﻿// Copyright 2021 Yubico AB
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

using Yubico.YubiKey.DeviceExtensions;
using Yubico.Core.Devices;
using Yubico.Core.Devices.Hid;
using Yubico.Core.Devices.SmartCard;
using System;

// Feature hold-back
#if false

namespace Yubico.YubiKey
{
    /// <summary>
    /// This partial class provides events for device arrival and removal.
    /// </summary>
    public partial class YubiKeyDevice
    {
        /// <summary>
        /// Event for device arrival.
        /// </summary>
        public static event EventHandler<YubiKeyDeviceEventArgs>? DeviceArrivedEvent;

        /// <summary>
        /// Event for device removal.
        /// </summary>
        public static event EventHandler<YubiKeyDeviceEventArgs>? DeviceRemovedEvent;

        /// <summary>
        /// Constructor for subscribing to events in Core.
        /// </summary>
        static YubiKeyDevice()
        {
            // this is prototype code for the future implementation
            //Yubico.Core.DeviceArrived += CoreDeviceArrived;
            //Yubico.Core.DeviceRemoved += CoreDeviceRemoved;
        }

        // this is prototype code for the future implementation
        //private static void CoreDeviceArrived(object sender, CoreDeviceEventArgs e)
        //{
        //    var deviceEventArgs = new YubiKeyDeviceEventArgs(e.device);
        //    OnDeviceArrived(deviceEventArgs);
        //}

        //private static void CoreDeviceRemoved(object sender, CoreDeviceEventArgs e)
        //{
        //    var deviceEventArgs = new YubiKeyDeviceEventArgs(e.device);
        //    OnDeviceRemoved(deviceEventArgs);
        //}

        /// <summary>
        /// Raises event on device arrival.
        /// </summary>
        private static void OnDeviceArrived(YubiKeyDeviceEventArgs e) => DeviceArrivedEvent?.Invoke(typeof(YubiKeyDevice), e);

        /// <summary>
        /// Raises event on device removal.
        /// </summary>
        private static void OnDeviceRemoved(YubiKeyDeviceEventArgs e) => DeviceRemovedEvent?.Invoke(typeof(YubiKeyDevice), e);
    }
}
#endif
