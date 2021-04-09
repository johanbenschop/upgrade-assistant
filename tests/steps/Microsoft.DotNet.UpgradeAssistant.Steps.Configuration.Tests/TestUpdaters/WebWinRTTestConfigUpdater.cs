﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.UpgradeAssistant.Steps.Configuration.Tests
{
    [ApplicableComponents(ProjectComponents.WinRT | ProjectComponents.AspNetCore)]
    public class WebWinRTTestConfigUpdater : IUpdater<ConfigFile>
    {
        private readonly bool _isApplicable;

        public string Id => "Test ConfigUpdater";

        public string Title => "Test title";

        public string Description => "Test description";

        public BuildBreakRisk Risk { get; }

        public int ApplyCount { get; set; }

        public WebWinRTTestConfigUpdater(BuildBreakRisk risk, bool isApplicable)
        {
            Risk = risk;
            _isApplicable = isApplicable;
        }

        public Task<bool> ApplyAsync(IUpgradeContext context, ImmutableArray<ConfigFile> inputs, CancellationToken token)
        {
            ApplyCount++;
            return Task.FromResult(_isApplicable);
        }

        public Task<bool> IsApplicableAsync(IUpgradeContext context, ImmutableArray<ConfigFile> inputs, CancellationToken token)
        {
            return Task.FromResult(_isApplicable);
        }
    }
}
