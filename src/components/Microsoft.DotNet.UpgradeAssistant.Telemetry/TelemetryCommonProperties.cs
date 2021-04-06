﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Options;

using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

namespace Microsoft.DotNet.UpgradeAssistant.Telemetry
{
    public class TelemetryCommonProperties : ITelemetryInitializer, IPropertyRetriever
    {
        private const string OSVersion = "OS Version";
        private const string OSPlatform = "OS Platform";
        private const string RuntimeId = "Runtime Id";
        private const string ProductVersion = "Product Version";
        private const string DockerContainer = "Docker Container";
        private const string MachineIdKey = "Machine ID";
        private const string KernelVersion = "Kernel Version";

        private const string MachineIdCacheKey = "MachineId";
        private const string IsDockerContainerCacheKey = "IsDockerContainer";

        public TelemetryCommonProperties(
            IOptions<TelemetryOptions> options,
            IDockerContainerDetector dockerContainerDetector,
            IStringHasher hasher,
            IMacAddressProvider macAddressProvider,
            IUserLevelCacheWriter userLevelCacheWriter)
        {
            _options = options;
            _hasher = hasher;
            _macAddressProvider = macAddressProvider;
            _dockerContainerDetector = dockerContainerDetector;
            _userLevelCacheWriter = userLevelCacheWriter;
        }

        private readonly IUserLevelCacheWriter _userLevelCacheWriter;
        private readonly IDockerContainerDetector _dockerContainerDetector;
        private readonly IOptions<TelemetryOptions> _options;
        private readonly IStringHasher _hasher;
        private readonly IMacAddressProvider _macAddressProvider;

        private Dictionary<string, string> GetTelemetryCommonProperties()
            => new()
            {
                { OSVersion, RuntimeEnvironment.OperatingSystemVersion },
                { OSPlatform, RuntimeEnvironment.OperatingSystemPlatform.ToString() },
                { RuntimeId, RuntimeEnvironment.GetRuntimeIdentifier() },
                { ProductVersion, _options.Value.ProductVersion },
                { DockerContainer, IsDockerContainer() },
                { MachineIdKey, GetMachineId() },
                { KernelVersion, GetKernelVersion() }
            };

        private string GetMachineId()
        {
            return _userLevelCacheWriter.RunWithCache(MachineIdCacheKey, () =>
            {
                var macAddress = _macAddressProvider.GetMacAddress();
                if (macAddress != null)
                {
                    return _hasher.Hash(macAddress);
                }
                else
                {
                    return Guid.NewGuid().ToString();
                }
            });
        }

        private string IsDockerContainer()
        {
            return _userLevelCacheWriter.RunWithCache(IsDockerContainerCacheKey, () =>
            {
                return _dockerContainerDetector.IsDockerContainer().ToString("G");
            });
        }

        /// <summary>
        /// Returns a string identifying the OS kernel.
        /// For Unix this currently comes from "uname -srv".
        /// For Windows this currently comes from RtlGetVersion().
        /// </summary>
        private static string GetKernelVersion()
        {
            return RuntimeInformation.OSDescription;
        }

        private Dictionary<string, string>? _properties;

        public void Initialize(ApplicationInsights.Channel.ITelemetry telemetry)
        {
            if (telemetry is not ISupportProperties t)
            {
                return;
            }

            foreach (var p in Properties)
            {
                t.Properties[p.Key] = p.Value;
            }
        }

        private Dictionary<string, string> Properties
        {
            get
            {
                if (_properties is null)
                {
                    _properties = GetTelemetryCommonProperties();
                }

                return _properties;
            }
        }

        public Guid MachineId
        {
            get
            {
                try
                {
                    if (Properties.TryGetValue(MachineIdKey, out var machineId) && machineId.Length >= 32)
                    {
                        var chopped = machineId.Substring(0, 32);
                        return new Guid(chopped);
                    }
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch
#pragma warning restore CA1031 // Do not catch general exception types
                {
                }

                return Guid.Empty;
            }
        }
    }
}
