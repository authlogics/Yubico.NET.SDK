// Copyright 2024 Yubico AB
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
using System.Collections.Generic;
using System.Text;

namespace Yubico.YubiKey
{
    /// <summary>
    /// Determines how the HID device finder should behave.
    /// </summary>
    public class YubiKeyHidDeviceFinderOptions
    {
        /// <summary>   
        /// Determines if device arrival and removal events should be listened for in the background
        /// </summary>
        public bool ListenForChanges { get; set; }

        /// <summary>   
        /// The transports that are detected during an update
        /// </summary>
        public Transport AllowedTransports { get; set; } = Transport.HidFido;
    }
}
