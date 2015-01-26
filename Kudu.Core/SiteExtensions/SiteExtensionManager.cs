using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Xml;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Settings;
using Kudu.Contracts.SiteExtensions;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;
using Kudu.Core.Settings;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using NuGet;
using NullLogger = Kudu.Core.Deployment.NullLogger;
using NuGet.Client;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Threading.Tasks;
using NuGet.Client.VisualStudio;
using NuGet.Versioning;
using System.Threading;
using System.Collections.Concurrent;

namespace Kudu.Core.SiteExtensions
{
    public class SiteExtensionManager : ISiteExtensionManager, IDisposable
    {
        private readonly SourceRepository _localRepository;
        private readonly CompositionContainer _container;
        private readonly IEnvironment _environment;
        private readonly IDeploymentSettingsManager _settings;
        private readonly ITraceFactory _traceFactory;

        private const string _applicationHostFile = "applicationHost.xdt";
        private const string _settingsFileName = "SiteExtensionSettings.json";
        private const string _feedUrlSetting = "feed_url";

        private readonly string _rootPath;
        private readonly string _baseUrl;
        private readonly IContinuousJobsManager _continuousJobManager;
        private readonly ITriggeredJobsManager _triggeredJobManager;

        private static readonly Dictionary<string, SiteExtensionInfo> _preInstalledExtensionDictionary
            = new Dictionary<string, SiteExtensionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "monaco",
                new SiteExtensionInfo
                {
                    Id = "Monaco",
                    Title = "Visual Studio Online",
                    Type = SiteExtensionInfo.SiteExtensionType.PreInstalledMonaco,
                    Authors = new [] {"Microsoft"},
                    IconUrl = "https://www.siteextensions.net/Content/Images/vso50x50.png",
                    LicenseUrl = "http://azure.microsoft.com/en-us/support/legal/",
                    ProjectUrl = "http://blogs.msdn.com/b/monaco/",
                    Description = "A full featured browser based development environment for editing your website",
                    // API will return a full url instead of this relative url.
                    ExtensionUrl = "/Dev"
                }
            },
            {
                "kudu",
                new SiteExtensionInfo
                {
                    Id = "Kudu",
                    Title = "Site Admin Tools",
                    Type = SiteExtensionInfo.SiteExtensionType.PreInstalledEnabled,
                    Authors = new [] {"Project Kudu Team"},
                    IconUrl = "https://www.siteextensions.net/Content/Images/kudu50x50.png",
                    LicenseUrl = "https://github.com/projectkudu/kudu/blob/master/LICENSE.txt",
                    ProjectUrl = "https://github.com/projectkudu/kudu",
                    Description = "Site administration and management tools for your Azure Websites, including a terminal, process viewer and more!",
                    // API will return a full url instead of this relative url.
                    ExtensionUrl = "/"
                }
            },
            {
                "daas",
                new SiteExtensionInfo
                {
                    Id = "Daas",
                    Title = "Diagnostics as a Service",
                    Type = SiteExtensionInfo.SiteExtensionType.PreInstalledEnabled,
                    Authors = new [] {"Microsoft"},
                    IconUrl = "https://www.siteextensions.net/Content/Images/DaaS50x50.png",
                    LicenseUrl = "http://azure.microsoft.com/en-us/support/legal/",
                    ProjectUrl = "http://azure.microsoft.com/blog/?p=157471",
                    Description = "Site diagnostic tools, including Event Viewer logs, memory dumps and http logs.",
                    // API will return a full url instead of this relative url.
                    ExtensionUrl = "/DaaS"
                }
            }
        };

        private const string _installScriptName = "install.cmd";
        private const string _uninstallScriptName = "uninstall.cmd";

        public SiteExtensionManager(IContinuousJobsManager continuousJobManager, ITriggeredJobsManager triggeredJobManager, IEnvironment environment, IDeploymentSettingsManager settings, ITraceFactory traceFactory, HttpContextBase context)
        {
            _rootPath = Path.Combine(environment.RootPath, "SiteExtensions");
            _baseUrl = context.Request.Url == null ? String.Empty : context.Request.Url.GetLeftPart(UriPartial.Authority).TrimEnd('/');

            var aggregateCatalog = new AggregateCatalog();
            var directoryCatalog = new DirectoryCatalog(@"E:\kudu\Kudu.Core\bin\Debug", "NuGet.Client*.dll");
            aggregateCatalog.Catalogs.Add(directoryCatalog);
            this._container = new CompositionContainer(aggregateCatalog);
            this._container.ComposeParts(this);

            this._localRepository = this.GetSourceRepository(this._rootPath);
            _continuousJobManager = continuousJobManager;
            _triggeredJobManager = triggeredJobManager;
            _environment = environment;
            _settings = settings;
            _traceFactory = traceFactory;

            IEnumerable<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>> providers = this._container.GetExports<INuGetResourceProvider, INuGetResourceProviderMetadata>();
            Console.WriteLine(providers.Count());
        }

        public async Task<IEnumerable<SiteExtensionInfo>> GetRemoteExtensions(string filter, bool allowPrereleaseVersions, string feedUrl)
        {
            var extensions = new List<SiteExtensionInfo>(GetPreInstalledExtensions(filter, showEnabledOnly: false));

            SourceRepository remoteRepo = this.GetRemoteRepository(feedUrl);

            IEnumerable<UISearchMetadata> packages =
                string.IsNullOrWhiteSpace(filter) ?     // TODO: investigate why we allow filter to be null, since it will take ages to query all the packages
                (await remoteRepo.Search(string.Empty)).OrderByDescending(p => p.LatestPackageMetadata.DownloadCount) :
                (await remoteRepo.Search(filter)).OrderByDescending(p => p.LatestPackageMetadata.DownloadCount);

            foreach (UISearchMetadata package in packages)
            {
                extensions.Add(await this.ConvertRemotePackageToSiteExtensionInfo(package, feedUrl));
            }

            return extensions;
        }

        public async Task<SiteExtensionInfo> GetRemoteExtension(string id, string version, string feedUrl)
        {
            SiteExtensionInfo info = GetPreInstalledExtension(id);
            if (info != null)
            {
                return info;
            }

            SourceRepository remoteRepo = this.GetRemoteRepository(feedUrl);
            UIPackageMetadata package =
                string.IsNullOrWhiteSpace(version) ?
                    await await remoteRepo.GetLatestPackageById(id) :
                    await await remoteRepo.GetPackageByIdentity(id, version);

            if (package == null)
            {
                return null;
            }

            info = await ConvertRemotePackageToSiteExtensionInfo(package, feedUrl);

            return info;
        }

        public async Task<IEnumerable<SiteExtensionInfo>> GetLocalExtensions(string filter, bool checkLatest)
        {
            IEnumerable<SiteExtensionInfo> preInstalledExtensions = GetPreInstalledExtensions(filter, showEnabledOnly: true);
            IEnumerable<UISearchMetadata> searchResult = await this._localRepository.Search(filter);

            List<SiteExtensionInfo> siteExtensionInfos = new List<SiteExtensionInfo>();
            foreach (var item in searchResult)
            {
                siteExtensionInfos.Add(await this.ConvertLocalPackageToSiteExtensionInfo(item, checkLatest));
            }

            return preInstalledExtensions.Concat(siteExtensionInfos);
        }

        public async Task<SiteExtensionInfo> GetLocalExtension(string id, bool checkLatest = true)
        {
            SiteExtensionInfo info = GetPreInstalledExtension(id);
            if (info != null && info.ExtensionUrl != null)
            {
                return info;
            }

            UIPackageMetadata package = await await this._localRepository.GetLatestPackageById(info.Id);
            if (package == null)
            {
                return null;
            }

            return await ConvertLocalPackageToSiteExtensionInfo(package, checkLatest);
        }

        private IEnumerable<SiteExtensionInfo> GetPreInstalledExtensions(string filter, bool showEnabledOnly)
        {
            var list = new List<SiteExtensionInfo>();

            foreach (SiteExtensionInfo extension in _preInstalledExtensionDictionary.Values)
            {
                if (String.IsNullOrEmpty(filter) ||
                    JsonConvert.SerializeObject(extension).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SiteExtensionInfo info = GetPreInstalledExtension(extension.Id);

                    if (!showEnabledOnly || info.ExtensionUrl != null)
                    {
                        list.Add(info);
                    }
                }
            }

            return list;
        }

        private SiteExtensionInfo GetPreInstalledExtension(string id)
        {
            if (_preInstalledExtensionDictionary.ContainsKey(id))
            {
                var info = new SiteExtensionInfo(_preInstalledExtensionDictionary[id]);

                SetLocalInfo(info);

                SetPreInstalledExtensionInfo(info);

                return info;
            }
            else
            {
                return null;
            }
        }

        public async Task<SiteExtensionInfo> InstallExtension(string id, string version, string feedUrl)
        {
            if (_preInstalledExtensionDictionary.ContainsKey(id))
            {
                return EnablePreInstalledExtension(_preInstalledExtensionDictionary[id]);
            }
            else
            {
                if (String.IsNullOrEmpty(feedUrl))
                {
                    feedUrl = GetSettingManager(id).GetValue(_feedUrlSetting);
                }

                SourceRepository remoteRepo = this.GetRemoteRepository(feedUrl);
                UIPackageMetadata localPackage = null;
                UIPackageMetadata repoPackage =
                    string.IsNullOrWhiteSpace(version) ?
                        await await remoteRepo.GetLatestPackageById(id) :
                        await await remoteRepo.GetPackageByIdentity(id, version);

                if (repoPackage != null)
                {
                    string installationDirectory = GetInstallationDirectory(id);

                    localPackage = await InstallExtension(repoPackage, installationDirectory, feedUrl);
                    GetSettingManager(id).SetValue(_feedUrlSetting, feedUrl);
                }

                return await ConvertLocalPackageToSiteExtensionInfo(localPackage, checkLatest: true);
            }
        }

        private async Task<UIPackageMetadata> InstallExtension(UIPackageMetadata package, string installationDirectory, string feedUrl)
        {
            try
            {
                if (FileSystemHelpers.DirectoryExists(installationDirectory))
                {
                    FileSystemHelpers.DeleteDirectorySafe(installationDirectory);
                }

                SourceRepository remoteRepo = this.GetRemoteRepository(feedUrl);

                // copy content folder
                await await remoteRepo.DownloadPackageToFolder(package.Identity, installationDirectory);

                // If there is no xdt file, generate default.
                GenerateApplicationHostXdt(installationDirectory, '/' + package.Identity.Id, isPreInstalled: false);

                OperationManager.Attempt(() => DeploySiteExtensionJobs(package.Identity.Id));

                var externalCommandFactory = new ExternalCommandFactory(_environment, _settings, installationDirectory);
                string installScript = Path.Combine(installationDirectory, _installScriptName);
                if (FileSystemHelpers.FileExists(installScript))
                {
                    OperationManager.Attempt(() =>
                    {
                        Executable exe = externalCommandFactory.BuildCommandExecutable(installScript,
                            installationDirectory,
                            _settings.GetCommandIdleTimeout(), NullLogger.Instance);
                        exe.ExecuteWithProgressWriter(NullLogger.Instance, _traceFactory.GetTracer(), String.Empty);
                    });
                }

                // Copy nupkg file for package list/lookup
                FileSystemHelpers.CreateDirectory(installationDirectory);
                string packageFilePath = GetNuGetPackageFile(package.Identity.Id, package.Identity.Version.ToString());
                var downloadResource = await remoteRepo.GetResourceAsync<DownloadResource>();
                using (
                    Stream readStream = await downloadResource.GetStream(package.Identity, CancellationToken.None),
                    writeStream = FileSystemHelpers.OpenWrite(packageFilePath))
                {
                    OperationManager.Attempt(() => readStream.CopyTo(writeStream));
                }
            }
            catch (Exception ex)
            {
                ITracer tracer = _traceFactory.GetTracer();
                tracer.TraceError(ex);
                FileSystemHelpers.DeleteDirectorySafe(installationDirectory);
                throw;
            }

            return await await _localRepository.GetLatestPackageById(package.Identity.Id);
        }

        private SiteExtensionInfo EnablePreInstalledExtension(SiteExtensionInfo info)
        {
            string id = info.Id;
            string installationDirectory = GetInstallationDirectory(id);

            try
            {
                if (FileSystemHelpers.DirectoryExists(installationDirectory))
                {
                    FileSystemHelpers.DeleteDirectorySafe(installationDirectory);
                }

                if (ExtensionRequiresApplicationHost(info))
                {
                    if (info.Type == SiteExtensionInfo.SiteExtensionType.PreInstalledMonaco)
                    {
                        GenerateApplicationHostXdt(installationDirectory,
                            _preInstalledExtensionDictionary[id].ExtensionUrl, isPreInstalled: true);
                    }
                }
                else
                {
                    FileSystemHelpers.CreateDirectory(installationDirectory);
                }
            }
            catch (Exception ex)
            {
                ITracer tracer = _traceFactory.GetTracer();
                tracer.TraceError(ex);
                FileSystemHelpers.DeleteDirectorySafe(installationDirectory);
                return null;
            }

            return GetPreInstalledExtension(id);
        }

        private static void GenerateApplicationHostXdt(string installationDirectory, string relativeUrl, bool isPreInstalled)
        {
            // If there is no xdt file, generate default.
            FileSystemHelpers.CreateDirectory(installationDirectory);
            string xdtPath = Path.Combine(installationDirectory, _applicationHostFile);
            if (!FileSystemHelpers.FileExists(xdtPath))
            {
                string xdtContent = CreateDefaultXdtFile(relativeUrl, isPreInstalled);
                OperationManager.Attempt(() => FileSystemHelpers.WriteAllText(xdtPath, xdtContent));
            }
        }

        public async Task<bool> UninstallExtension(string id)
        {
            string installationDirectory = GetInstallationDirectory(id);

            SiteExtensionInfo info = await this.GetLocalExtension(id, checkLatest: false);

            if (info == null || !FileSystemHelpers.DirectoryExists(info.LocalPath))
            {
                throw new DirectoryNotFoundException(installationDirectory);
            }

            var externalCommandFactory = new ExternalCommandFactory(_environment, _settings, installationDirectory);

            string uninstallScript = Path.Combine(installationDirectory, _uninstallScriptName);

            if (FileSystemHelpers.FileExists(uninstallScript))
            {
                OperationManager.Attempt(() =>
                {
                    Executable exe = externalCommandFactory.BuildCommandExecutable(uninstallScript,
                        installationDirectory,
                        _settings.GetCommandIdleTimeout(), NullLogger.Instance);
                    exe.ExecuteWithProgressWriter(NullLogger.Instance, _traceFactory.GetTracer(), String.Empty);
                });
            }

            OperationManager.Attempt(() => CleanupSiteExtensionJobs(id));

            OperationManager.Attempt(() => FileSystemHelpers.DeleteFileSafe(GetNuGetPackageFile(info.Id, info.Version)));

            OperationManager.Attempt(() => FileSystemHelpers.DeleteDirectorySafe(installationDirectory));

            return await this.GetLocalExtension(id, checkLatest: false) == null;
        }

        private SourceRepository GetRemoteRepository(string feedUrl)
        {
            return string.IsNullOrWhiteSpace(feedUrl) ?
                this.GetSourceRepository(this._settings.GetSiteExtensionRemoteUrl()) :
                this.GetSourceRepository(feedUrl);
        }

        private string GetInstallationDirectory(string id)
        {
            return Path.Combine(this._localRepository.PackageSource.Source, id);
        }

        private JsonSettings GetSettingManager(string id)
        {
            string filePath = Path.Combine(_rootPath, id, _settingsFileName);

            return new JsonSettings(filePath);
        }

        private string GetNuGetPackageFile(string id, string version)
        {
            return Path.Combine(GetInstallationDirectory(id), String.Format("{0}.{1}.nupkg", id, version));
        }

        private static string GetPreInstalledDirectory(string id)
        {
            string programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);
            return Path.Combine(programFiles, "SiteExtensions", id);
        }

        private static string CreateDefaultXdtFile(string relativeUrl, bool isPreInstalled)
        {
            string physicalPath = isPreInstalled ? "%XDT_LATEST_EXTENSIONPATH%" : "%XDT_EXTENSIONPATH%";
            string template = null;

            using (Stream stream = typeof(SiteExtensionManager).Assembly.GetManifestResourceStream("Kudu.Core.SiteExtensions." + _applicationHostFile + ".xml"))
            using (StreamReader reader = new StreamReader(stream))
            {
                template = reader.ReadToEnd();
            }

            return String.Format(template, relativeUrl, physicalPath);
        }

        private void SetLocalInfo(SiteExtensionInfo info)
        {
            string localPath = GetInstallationDirectory(info.Id);
            if (FileSystemHelpers.DirectoryExists(localPath))
            {
                info.LocalPath = localPath;
                info.InstalledDateTime = FileSystemHelpers.GetLastWriteTimeUtc(info.LocalPath);
            }

            if (ExtensionRequiresApplicationHost(info))
            {
                info.ExtensionUrl = FileSystemHelpers.FileExists(Path.Combine(localPath, _applicationHostFile))
                    ? GetFullUrl(GetUrlFromApplicationHost(info)) : null;
            }
            else if (String.Equals(info.Id, "Monaco", StringComparison.OrdinalIgnoreCase))
            {
                // Monaco does not need ApplicationHost only when it is enabled through app setting
                info.ExtensionUrl = GetFullUrl(info.ExtensionUrl);
            }
            else
            {
                info.ExtensionUrl = String.IsNullOrEmpty(info.LocalPath) ? null : GetFullUrl(info.ExtensionUrl);
            }

            info.FeedUrl = GetSettingManager(info.Id).GetValue(_feedUrlSetting);
        }

        private static string GetUrlFromApplicationHost(SiteExtensionInfo info)
        {
            try
            {
                var appHostDoc = new XmlDocument();
                appHostDoc.Load(Path.Combine(info.LocalPath, _applicationHostFile));

                // Get the 'path' property of the first 'application' element, which is the relative url.
                XmlNode pathPropertyNode = appHostDoc.SelectSingleNode("//application[@path]/@path");

                return pathPropertyNode.Value;
            }
            catch (SystemException)
            {
                return null;
            }
        }

        private void DeploySiteExtensionJobs(string siteExtensionName)
        {
            string siteExtensionPath = Path.Combine(_rootPath, siteExtensionName);
            _continuousJobManager.SyncExternalJobs(siteExtensionPath, siteExtensionName);
            _triggeredJobManager.SyncExternalJobs(siteExtensionPath, siteExtensionName);
        }

        private void CleanupSiteExtensionJobs(string siteExtensionName)
        {
            _continuousJobManager.CleanupExternalJobs(siteExtensionName);
            _triggeredJobManager.CleanupExternalJobs(siteExtensionName);
        }

        private async Task<SiteExtensionInfo> ConvertRemotePackageToSiteExtensionInfo(UISearchMetadata package, string feedUrl)
        {
            return await this.CheckRemotePackageLatestVersion(new SiteExtensionInfo(package), feedUrl);
        }

        private async Task<SiteExtensionInfo> ConvertRemotePackageToSiteExtensionInfo(UIPackageMetadata package, string feedUrl)
        {
            return await this.CheckRemotePackageLatestVersion(new SiteExtensionInfo(package), feedUrl);
        }

        private async Task<SiteExtensionInfo> CheckRemotePackageLatestVersion(SiteExtensionInfo info, string feedUrl)
        {
            info.FeedUrl = feedUrl;
            UIPackageMetadata localPackage = await await this._localRepository.GetLatestPackageById(info.Id);

            if (localPackage != null)
            {
                SetLocalInfo(info);
                // Assume input package (from remote) is always the latest version.
                info.LocalIsLatestVersion = NuGetVersion.Parse(info.Version).Equals(localPackage.Identity.Version);
            }

            return info;
        }

        private async Task<SiteExtensionInfo> ConvertLocalPackageToSiteExtensionInfo(UIPackageMetadata package, bool checkLatest)
        {
            if (package == null)
            {
                return null;
            }

            var info = new SiteExtensionInfo(package);
            SetLocalInfo(info);
            await this.TryCheckLocalPackageLatestVersionFromRemote(info, checkLatest);
            return info;
        }

        private async Task<SiteExtensionInfo> ConvertLocalPackageToSiteExtensionInfo(UISearchMetadata package, bool checkLatest)
        {
            if (package == null)
            {
                return null;
            }

            var info = new SiteExtensionInfo(package);
            SetLocalInfo(info);
            await this.TryCheckLocalPackageLatestVersionFromRemote(info, checkLatest);
            return info;
        }

        private async Task TryCheckLocalPackageLatestVersionFromRemote(SiteExtensionInfo info, bool checkLatest)
        {
            if (checkLatest)
            {
                // FindPackage gets back the latest version.
                SourceRepository remoteRepo = GetRemoteRepository(info.FeedUrl);
                UIPackageMetadata latestPackage = await await remoteRepo.GetLatestPackageById(info.Id);
                if (latestPackage != null)
                {
                    NuGetVersion currentVersion = NuGetVersion.Parse(info.Version);
                    info.LocalIsLatestVersion = NuGetVersion.Parse(info.Version).Equals(latestPackage.Identity.Version);
                    info.DownloadCount = latestPackage.DownloadCount;
                    info.PublishedDateTime = latestPackage.Published;
                }
            }
        }

        private static bool ExtensionRequiresApplicationHost(SiteExtensionInfo info)
        {
            string appSettingName = info.Id.ToUpper(CultureInfo.CurrentCulture) + "_EXTENSION_VERSION";
            bool enabledInSetting = ConfigurationManager.AppSettings[appSettingName] == "beta";
            return !(enabledInSetting || info.Type == SiteExtensionInfo.SiteExtensionType.PreInstalledEnabled);
        }

        private static void SetPreInstalledExtensionInfo(SiteExtensionInfo info)
        {
            string directory = GetPreInstalledDirectory(info.Id);

            if (FileSystemHelpers.DirectoryExists(directory))
            {
                if (info.Type == SiteExtensionInfo.SiteExtensionType.PreInstalledMonaco)
                {
                    info.Version = GetPreInstalledLatestVersion(directory);
                }
                else if (info.Type == SiteExtensionInfo.SiteExtensionType.PreInstalledEnabled)
                {
                    info.Version = typeof(SiteExtensionManager).Assembly.GetName().Version.ToString();
                }

                info.PublishedDateTime = FileSystemHelpers.GetLastWriteTimeUtc(directory);
            }
            else
            {
                info.Version = null;
                info.PublishedDateTime = null;
            }

            info.LocalIsLatestVersion = true;
        }

        private static string GetPreInstalledLatestVersion(string directory)
        {
            if (!FileSystemHelpers.DirectoryExists(directory))
            {
                return null;
            }

            string[] pathStrings = FileSystemHelpers.GetDirectories(directory);

            if (pathStrings.Length == 0)
            {
                return null;
            }

            return pathStrings.Max(path =>
            {
                string versionString = FileSystemHelpers.DirectoryInfoFromDirectoryName(path).Name;
                SemanticVersion semVer;
                if (SemanticVersion.TryParse(versionString, out semVer))
                {
                    return semVer;
                }
                else
                {
                    return new SemanticVersion(0, 0, 0, 0);
                }
            }).ToString();
        }

        private string GetFullUrl(string url)
        {
            return url == null ? null : new Uri(new Uri(_baseUrl), url).ToString().Trim('/') + "/";
        }

        /// <summary>
        /// Create SourceRepository from given feed endpoint
        /// </summary>
        /// <param name="feedEndpoint">V2 or V3 feed endpoint</param>
        /// <returns>SourceRepository object</returns>
        private SourceRepository GetSourceRepository(string feedEndpoint)
        {
            IEnumerable<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>> providers = this._container.GetExports<INuGetResourceProvider, INuGetResourceProviderMetadata>();
            NuGet.Configuration.PackageSource source = new NuGet.Configuration.PackageSource(feedEndpoint);
            return new SourceRepository(source, providers);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // reference https://msdn.microsoft.com/en-us/library/ms244737.aspx
            if (disposing && this._container != null)
            {
                this._container.Dispose();
            }
        }

        public T GetResource<T>(string feedEndpoint)
        {
            IEnumerable<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>> providers = this._container.GetExports<INuGetResourceProvider, INuGetResourceProviderMetadata>();
            NuGet.Configuration.PackageSource source = new NuGet.Configuration.PackageSource(feedEndpoint);
            var repo = new SourceRepository(source, providers);
            var _providerCache = Init_DELETE_ME(providers);

            try
            {
                Type resourceType = typeof(T);
                INuGetResource resource = null;
                
                Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>[] possible = null;

                if (_providerCache.TryGetValue(resourceType, out possible))
                {
                    foreach (var provider in possible)
                    {
                        if (provider.Value.TryCreate(repo, out resource))
                        {
                            // found
                            break;
                        }
                    }
                }

                return resource == null ? default(T) : (T)resource;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return default(T);
            }
        }

        private static Dictionary<Type, Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>[]>
            Init_DELETE_ME(IEnumerable<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>> providers)
        {
            var cache = new Dictionary<Type, Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>[]>();

            foreach (var group in providers.GroupBy(p => p.Metadata.ResourceType))
            {
                cache.Add(group.Key, Sort(group).ToArray());
            }

            return cache;
        }

        private static Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>[]
            Sort(IEnumerable<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>> group)
        {
            // initial ordering to help make this deterministic 
            var items = new List<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>>(
                group.OrderBy(e => e.Metadata.Name).ThenBy(e => e.Metadata.After.Count()).ThenBy(e => e.Metadata.Before.Count()));


            ProviderComparer comparer = new ProviderComparer();

            var ordered = new Queue<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>>();

            // List.Sort does not work when lists have unsolvable gaps, which can occur here
            while (items.Count > 0)
            {
                Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata> best = items[0];

                for (int i = 1; i < items.Count; i++)
                {
                    if (comparer.Compare(items[i], best) < 0)
                    {
                        best = items[i];
                    }
                }

                items.Remove(best);
                ordered.Enqueue(best);
            }

            return ordered.ToArray();
        }
    }

    /// <summary>
    /// An imperfect sort for provider before/after
    /// </summary>
    internal class ProviderComparer : IComparer<Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>>
    {

        public ProviderComparer()
        {

        }

        // higher goes last
        public int Compare(Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata> providerA, Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata> providerB)
        {
            INuGetResourceProviderMetadata x = providerA.Metadata;
            INuGetResourceProviderMetadata y = providerB.Metadata;

            if (StringComparer.Ordinal.Equals(x.Name, y.Name))
            {
                return 0;
            }

            // empty names go last
            if (String.IsNullOrEmpty(x.Name))
            {
                return 1;
            }

            if (String.IsNullOrEmpty(y.Name))
            {
                return -1;
            }

            // check x 
            if (x.Before.Contains(y.Name, StringComparer.Ordinal))
            {
                return -1;
            }

            if (x.After.Contains(y.Name, StringComparer.Ordinal))
            {
                return 1;
            }

            // check y
            if (y.Before.Contains(x.Name, StringComparer.Ordinal))
            {
                return 1;
            }

            if (y.After.Contains(x.Name, StringComparer.Ordinal))
            {
                return -1;
            }

            // compare with the known names
            if ((x.Before.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal) || (x.After.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal)))
                && !(y.Before.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal) || (y.After.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal))))
            {
                return 1;
            }

            if ((y.Before.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal) || (y.After.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal)))
                && !(x.Before.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal) || (x.After.Contains(NuGetResourceProviderPositions.Last, StringComparer.Ordinal))))
            {
                return -1;
            }

            if ((x.Before.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal) || (x.After.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal)))
                && !(y.Before.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal) || (y.After.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal))))
            {
                return -1;
            }

            if ((y.Before.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal) || (y.After.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal)))
                && !(x.Before.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal) || (x.After.Contains(NuGetResourceProviderPositions.First, StringComparer.Ordinal))))
            {
                return 1;
            }

            // give up and sort based on the name
            return StringComparer.Ordinal.Compare(x.Name, y.Name);
        }
    }
}
