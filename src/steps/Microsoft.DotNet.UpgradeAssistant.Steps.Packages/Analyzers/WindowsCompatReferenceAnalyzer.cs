﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.UpgradeAssistant.Dependencies;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.UpgradeAssistant.Steps.Packages.Analyzers
{
    public class WindowsCompatReferenceAnalyzer : IDependencyAnalyzer
    {
        private const string PackageName = "Microsoft.Windows.Compatibility";

        private readonly IPackageLoader _loader;
        private readonly IVersionComparer _comparer;
        private readonly ILogger<WindowsCompatReferenceAnalyzer> _logger;

        public WindowsCompatReferenceAnalyzer(
            IPackageLoader loader,
            IVersionComparer comparer,
            ILogger<WindowsCompatReferenceAnalyzer> logger)
        {
            _logger = logger;
            _loader = loader;
            _comparer = comparer;
        }

        public string Name => "Windows Compatibility Pack Analyzer";

        public async Task AnalyzeAsync(IProject project, IDependencyAnalysisState state, CancellationToken token)
        {
            if (project is null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!project.TargetFrameworks.Any(tfm => tfm.IsWindows))
            {
                return;
            }

            if (await project.NuGetReferences.IsTransitivelyAvailableAsync(PackageName, token).ConfigureAwait(false))
            {
                _logger.LogDebug("{PackageName} already referenced transitively", PackageName);
                return;
            }

            var latestVersion = await _loader.GetLatestVersionAsync(PackageName, project.TargetFrameworks, false, token).ConfigureAwait(false);

            if (latestVersion is null)
            {
                _logger.LogWarning("Could not find {PackageName}", latestVersion);
                return;
            }

            if (project.NuGetReferences.TryGetPackageByName(PackageName, out var existing))
            {
                if (_comparer.Compare(existing.Version, latestVersion.Version) >= 0)
                {
                    return;
                }

                state.Packages.Remove(existing);
            }

            _logger.LogInformation("Adding {PackageName} {Version}", PackageName, latestVersion.Version);

            state.Packages.Add(new NuGetReference(PackageName, latestVersion.Version));
        }
    }
}
