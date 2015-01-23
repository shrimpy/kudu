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
using System.Reactive.Linq;
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
        /// Query all result, will include pre-released package by default
        /// </summary>
        public static Task<Task<IEnumerable<UISearchMetadata>>> Search(this SourceRepository srcRepo, string searchTerm, SearchFilter filter = null)
        {
            return Task.Factory.StartNew<Task<IEnumerable<UISearchMetadata>>>(
                async () =>
                {
                    var searchResource = await srcRepo.GetResourceAsync<UISearchResource>();

                    int skip = 0;
                    int take = 1000;

                    var cancellationTokenSrc = new CancellationTokenSource();
                    IEnumerable<UISearchMetadata> searchResult = null;

                    // always include pre-release package
                    if (filter == null)
                    {
                        filter = new SearchFilter();
                    }

                    filter.IncludePrerelease = true;

                    return Observable.Create<UISearchMetadata>(
                          async obs =>
                          {
                              do
                              {
                                  try
                                  {
                                      searchResult = await searchResource.Search(searchTerm, filter, skip, take, cancellationTokenSrc.Token);
                                      foreach (var item in searchResult)
                                      {
                                          obs.OnNext(item);
                                      }

                                      skip = skip + take;
                                  }
                                  catch
                                  {
                                      // TODO: logging
                                      cancellationTokenSrc.Cancel();
                                      searchResult = null;
                                  }
                              } while (!cancellationTokenSrc.IsCancellationRequested && searchResult.Count() == take);

                              obs.OnCompleted();
                          }).ToEnumerable<UISearchMetadata>();
                });
        }

        /// <summary>
        /// Query source repository for latest package base given package id
        /// </summary>
        public static Task<Task<UIPackageMetadata>> GetLatestPackageById(this SourceRepository srcRepo, string packageId)
        {
            return Task.Factory.StartNew<Task<UIPackageMetadata>>(
                async () =>
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
                });
        }

        /// <summary>
        /// Query source repository for a package base given package id and version
        /// </summary>
        public static Task<Task<UIPackageMetadata>> GetPackageByIdentity(this SourceRepository srcRepo, string packageId, string version)
        {
            return Task.Factory.StartNew<Task<UIPackageMetadata>>(
                async () =>
                {
                    var metadataResource = await srcRepo.GetResourceAsync<UIMetadataResource>();
                    IEnumerable<UIPackageMetadata> packages = await metadataResource.GetMetadata(
                        new NuGet.PackagingCore.PackageIdentity(packageId, NuGetVersion.Parse(version)),
                        true,
                        true,
                        CancellationToken.None);

                    UIPackageMetadata[] packagesArray = packages.ToArray<UIPackageMetadata>();
                    if (packagesArray.Length == 0)
                    {
                        return null;
                    }
                    else
                    {
                        return packagesArray[0];
                    }
                });
        }

        /// <summary>
        /// Helper function to download package from given url and place content (only 'content' folder from package) to given folder
        /// </summary>
        public static Task<Task> DownloadPackageToFolder(this SourceRepository srcRepo, PackageIdentity identity, string localFolderPath)
        {
            return Task.Factory.StartNew<Task>(
                async () =>
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
                });
        }
    }
}
