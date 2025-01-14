// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ApiCompatibility.Logging;
using Microsoft.DotNet.ApiCompatibility.Runner;
using NuGet.ContentModel;
using NuGet.Frameworks;

namespace Microsoft.DotNet.PackageValidation.Validators
{
    /// <summary>
    /// Validates that there are compile time and runtime assets for all the compatible frameworks.
    /// Queues APICompat work items for the applicable compile and runtime assemblies for these frameworks.
    /// </summary>
    public class CompatibleTfmValidator(ISuppressibleLog log,
        IApiCompatRunner apiCompatRunner) : IPackageValidator
    {
        private static readonly Dictionary<NuGetFramework, HashSet<NuGetFramework>> s_packageTfmMapping = InitializeTfmMappings();

        /// <summary>
        /// Validates that there are compile time and runtime assets for all the compatible frameworks.
        /// Validates that the surface between compile time and runtime assets is compatible.
        /// </summary>
        /// <param name="options"><see cref="PackageValidatorOption"/> to configure the compatible TFM package validation.</param>
        public void Validate(PackageValidatorOption options)
        {
            ApiCompatRunnerOptions apiCompatOptions = new(options.EnableStrictMode);

            HashSet<NuGetFramework> compatibleTargetFrameworks = [];
            foreach (NuGetFramework item in options.Package.FrameworksInPackage)
            {
                compatibleTargetFrameworks.Add(item);
                if (s_packageTfmMapping.ContainsKey(item))
                {
                    compatibleTargetFrameworks.UnionWith(s_packageTfmMapping[item]);
                }
            }

            foreach (NuGetFramework framework in compatibleTargetFrameworks)
            {
                IReadOnlyList<ContentItem>? compileTimeAsset = options.Package.FindBestCompileAssetForFramework(framework);
                if (compileTimeAsset == null)
                {
                    log.LogError(new Suppression(DiagnosticIds.ApplicableCompileTimeAsset,
                        string.Format(Resources.NoCompatibleCompileTimeAsset, framework),
                        target: framework.ToString()));
                    break;
                }

                IReadOnlyList<ContentItem>? runtimeAsset = options.Package.FindBestRuntimeAssetForFramework(framework);
                if (runtimeAsset == null)
                {
                    log.LogError(new Suppression(DiagnosticIds.CompatibleRuntimeRidLessAsset,
                        string.Format(Resources.NoCompatibleRuntimeAsset, framework),
                        framework.ToString()));
                }
                // Invoke ApiCompat to compare the compile time asset with the runtime asset if they are not the same assembly.
                else if (options.EnqueueApiCompatWorkItems)
                {
                    apiCompatRunner.QueueApiCompatFromContentItem(log,
                        compileTimeAsset,
                        runtimeAsset,
                        apiCompatOptions,
                        options.Package);
                }

                foreach (string rid in options.Package.Rids.Where(packageRid => framework.SupportsRuntimeIdentifier(packageRid)))
                {
                    IReadOnlyList<ContentItem>? runtimeRidSpecificAsset = options.Package.FindBestRuntimeAssetForFrameworkAndRuntime(framework, rid);
                    if (runtimeRidSpecificAsset == null)
                    {
                        log.LogError(new Suppression(DiagnosticIds.CompatibleRuntimeRidSpecificAsset,
                            string.Format(Resources.NoCompatibleRidSpecificRuntimeAsset,
                                framework,
                                rid),
                            framework.ToString() + "-" + rid));
                    }
                    // Invoke ApiCompat to compare the compile time asset with the runtime specific asset if they are not the same and
                    // if the comparison hasn't already happened (when the runtime asset is the same as the runtime specific asset).
                    else if (options.EnqueueApiCompatWorkItems)
                    {
                        apiCompatRunner.QueueApiCompatFromContentItem(log,
                            compileTimeAsset,
                            runtimeRidSpecificAsset,
                            apiCompatOptions,
                            options.Package);
                    }
                }
            }

            if (options.ExecuteApiCompatWorkItems)
            {
                apiCompatRunner.ExecuteWorkItems();
            }
        }

        private static Dictionary<NuGetFramework, HashSet<NuGetFramework>> InitializeTfmMappings()
        {
            Dictionary<NuGetFramework, HashSet<NuGetFramework>> packageTfmMapping = [];

            // creating a map framework in package => frameworks to test based on default compatibility mapping.
            foreach (OneWayCompatibilityMappingEntry item in DefaultFrameworkMappings.Instance.CompatibilityMappings)
            {
                NuGetFramework forwardTfm = item.SupportedFrameworkRange.Max;
                NuGetFramework reverseTfm = item.TargetFrameworkRange.Min;
#if NET
                if (packageTfmMapping.TryGetValue(forwardTfm, out HashSet<NuGetFramework>? value))
                {
                    value.Add(reverseTfm);
                }
#else
                if (packageTfmMapping.ContainsKey(forwardTfm))
                {
                    packageTfmMapping[forwardTfm].Add(reverseTfm);
                }
#endif
                else
                {
                    packageTfmMapping.Add(forwardTfm, [ reverseTfm ]);
                }
            }

            return packageTfmMapping;
        }
    }
}
