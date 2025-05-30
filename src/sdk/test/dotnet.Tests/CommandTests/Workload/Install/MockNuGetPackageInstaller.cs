// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using Microsoft.DotNet.Cli.ToolPackage;
using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Microsoft.DotNet.Cli.NuGetPackageDownloader
{
    internal class MockNuGetPackageDownloader : INuGetPackageDownloader
    {
        private readonly string _downloadPath;
        private readonly bool _manifestDownload;
        private NuGetVersion _lastPackageVersion = new("1.0.0");
        private IEnumerable<NuGetVersion> _packageVersions;

        public List<(PackageId id, NuGetVersion version, DirectoryPath? downloadFolder, PackageSourceLocation packageSourceLocation)> DownloadCallParams = new();

        public List<string> DownloadCallResult = new();

        public List<(string, DirectoryPath)> ExtractCallParams = new();

        public HashSet<string> PackageIdsToNotFind { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public MockNuGetPackageDownloader(string dotnetRoot = null, bool manifestDownload = false, IEnumerable<NuGetVersion> packageVersions = null)
        {
            _manifestDownload = manifestDownload;
            _downloadPath = dotnetRoot == null ? string.Empty : Path.Combine(dotnetRoot, "metadata", "temp");
            if (_downloadPath != string.Empty)
            {
                Directory.CreateDirectory(_downloadPath);
            }

            _packageVersions = packageVersions;
        }

        public Task<string> DownloadPackageAsync(PackageId packageId,
            NuGetVersion packageVersion = null,
            PackageSourceLocation packageSourceLocation = null,
            bool includePreview = false,
            bool? includeUnlisted = null,
            DirectoryPath? downloadFolder = null,
            PackageSourceMapping packageSourceMapping = null)
        {
            DownloadCallParams.Add((packageId, packageVersion, downloadFolder, packageSourceLocation));

            if (PackageIdsToNotFind.Contains(packageId.ToString()))
            {
                return Task.FromException<string>(new NuGetPackageNotFoundException("Package not found: " + packageId.ToString()));
            }

            var path = Path.Combine(_downloadPath, "mock.nupkg");
            DownloadCallResult.Add(path);
            if (_downloadPath != string.Empty)
            {
                try
                {
                    File.WriteAllText(path, string.Empty);
                }
                catch (IOException)
                {
                    // Do not write this file twice in parallel
                }
            }
            _lastPackageVersion = packageVersion ?? new NuGetVersion("1.0.42");
            return Task.FromResult(path);
        }

        public Task<IEnumerable<string>> ExtractPackageAsync(string packagePath, DirectoryPath targetFolder)
        {
            ExtractCallParams.Add((packagePath, targetFolder));
            if (_manifestDownload)
            {
                var dataFolder = Path.Combine(targetFolder.Value, "data");
                Directory.CreateDirectory(dataFolder);
                string manifestContents = $@"{{
  ""version"": ""{_lastPackageVersion.ToString()}"",
  ""workloads"": {{
    }}
  }},
  ""packs"": {{
  }}
}}";

                File.WriteAllText(Path.Combine(dataFolder, "WorkloadManifest.json"), manifestContents);
            }

            return Task.FromResult(new List<string>() as IEnumerable<string>);
        }

        public Task<IEnumerable<NuGetVersion>> GetLatestPackageVersions(PackageId packageId, int numberOfResults, PackageSourceLocation packageSourceLocation = null, bool includePreview = false) => Task.FromResult(_packageVersions ?? Enumerable.Empty<NuGetVersion>());

        public Task<NuGetVersion> GetLatestPackageVersion(PackageId packageId, PackageSourceLocation packageSourceLocation = null, bool includePreview = false)
        {
            return Task.FromResult(new NuGetVersion("10.0.0"));
        }

        public Task<NuGetVersion> GetBestPackageVersionAsync(PackageId packageId, VersionRange versionRange, PackageSourceLocation packageSourceLocation = null)
        {
            return Task.FromResult(new NuGetVersion("10.0.0"));
        }



        public Task<string> GetPackageUrl(PackageId packageId,
            NuGetVersion packageVersion,
            PackageSourceLocation packageSourceLocation = null,
            bool includePreview = false)
        {
            return Task.FromResult($"http://mock-url/{packageId}.{packageVersion}.nupkg");
        }
    }
}
