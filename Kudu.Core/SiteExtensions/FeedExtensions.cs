using Kudu.Core.Infrastructure;
using NuGet.Client;
using NuGet.Client.VisualStudio;
using NuGet.PackagingCore;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Core.SiteExtensions
{
    /// <summary>
    /// Helper function to query feed
    /// </summary>
    public static class FeedExtensions
    {
        /// <summary>
        /// Query result by search term, will include pre-released package by default
        /// </summary>
        public static async Task<IEnumerable<UISearchMetadata>> Search(this SourceRepository srcRepo, string searchTerm, SearchFilter filter = null, int skip = 0, int take = 1000)
        {
            // always include pre-release package
            if (filter == null)
            {
                filter = new SearchFilter();
            }

            filter.IncludePrerelease = true;
            var searchResource = await srcRepo.GetResourceAsync<UISearchResource>();
            return await searchResource.Search(searchTerm, filter, skip, take, CancellationToken.None);
        }

        /// <summary>
        /// Query source repository for latest package base given package id
        /// </summary>
        public static async Task<UIPackageMetadata> GetLatestPackageById(this SourceRepository srcRepo, string packageId)
        {
            UIPackageMetadata latestPackage = null;
            var metadataResource = await srcRepo.GetResourceAsync<UIMetadataResource>();
            IEnumerable<UIPackageMetadata> packages = await metadataResource.GetMetadata(packageId, true, true, CancellationToken.None);
            foreach (var p in packages)
            {
                if (latestPackage == null ||
                    latestPackage.Identity.Version < p.Identity.Version)
                {
                    latestPackage = p;
                }
            }

            return latestPackage;
        }

        /// <summary>
        /// Query source repository for a package base given package id and version
        /// </summary>
        public static async Task<UIPackageMetadata> GetPackageByIdentity(this SourceRepository srcRepo, string packageId, string version)
        {
            var metadataResource = await srcRepo.GetResourceAsync<UIMetadataResource>();
            IEnumerable<UIPackageMetadata> packages = await metadataResource.GetMetadata(packageId, true, true, CancellationToken.None);
            NuGetVersion expectedVersion = NuGetVersion.Parse(version);
            return packages.FirstOrDefault((p) => p.Identity.Version.Equals(expectedVersion));
        }

        /// <summary>
        /// Helper function to download package from given url and place content (only 'content' folder from package) to given folder
        /// </summary>
        public static async Task DownloadPackageToFolder(this SourceRepository srcRepo, PackageIdentity identity, string localFolderPath)
        {
            string tmpFolderToExtract = Path.Combine(localFolderPath, Guid.NewGuid().ToString().Substring(0, 8));

            try
            {
                FileSystemHelpers.EnsureDirectory(tmpFolderToExtract);

                var downloadResource = await srcRepo.GetResourceAsync<DownloadResource>();
                using (Stream downloadStream = await downloadResource.GetStream(identity, CancellationToken.None))
                using (ZipArchive zip = new ZipArchive(downloadStream))
                {
                    zip.Extract(tmpFolderToExtract);

                    // kudu only care about things under "content" folder
                    string contentFolder = FileSystemHelpers.GetDirectories(tmpFolderToExtract).FirstOrDefault(p => p.EndsWith("\\content"));
                    if (!string.IsNullOrWhiteSpace(contentFolder))
                    {
                        string[] entries = FileSystemHelpers.GetFileSystemEntries(contentFolder);

                        foreach (string entry in entries)
                        {
                            FileAttributes fileAttr = FileSystemHelpers.GetAttributes(entry);
                            if ((fileAttr & FileAttributes.Directory) == FileAttributes.Directory)
                            {
                                DirectoryInfo dirInfo = new DirectoryInfo(entry);
                                FileSystemHelpers.MoveDirectory(entry, Path.Combine(localFolderPath, dirInfo.Name));
                            }
                            else
                            {
                                FileInfo fileInfo = new FileInfo(entry);
                                FileSystemHelpers.MoveFile(entry, Path.Combine(localFolderPath, fileInfo.Name));
                            }
                        }
                    }
                }
            }
            finally
            {
                FileSystemHelpers.DeleteDirectorySafe(tmpFolderToExtract);
            }
        }
    }
}
