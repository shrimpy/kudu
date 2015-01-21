using System;
using System.Collections.Generic;
using NuGet;
using Newtonsoft.Json;
using NuGet.Client.VisualStudio;

namespace Kudu.Contracts.SiteExtensions
{
    // This is equivalent to NuGet.IPackage
    public class SiteExtensionInfo
    {
        public enum SiteExtensionType
        {
            Gallery,
            PreInstalledMonaco,
            PreInstalledEnabled
        }

        public SiteExtensionInfo()
        {
        }

        public SiteExtensionInfo(SiteExtensionInfo info)
        {
            Id = info.Id;
            Title = info.Title;
            Type = info.Type;
            Summary = info.Summary;
            Description = info.Description;
            Version = info.Version;
            ExtensionUrl = info.ExtensionUrl;
            ProjectUrl = info.ProjectUrl;
            IconUrl = info.IconUrl;
            LicenseUrl = info.LicenseUrl;
            Authors = info.Authors;
            PublishedDateTime = info.PublishedDateTime;
            IsLatestVersion = info.IsLatestVersion;
            DownloadCount = info.DownloadCount;
            LocalIsLatestVersion = info.LocalIsLatestVersion;
            LocalPath = info.LocalPath;
            InstalledDateTime = info.InstalledDateTime;
        }

        public SiteExtensionInfo(UIPackageMetadata data)
        {
            this.Id = data.Identity.Id;
            this.Title = data.Identity.Id;
            this.Type = SiteExtensionType.Gallery;
            this.Summary = data.Summary;
            this.Description = data.Description;
            this.Version = data.Identity.Version.ToNormalizedString();
            this.ProjectUrl = data.ProjectUrl == null ? null : data.ProjectUrl.ToString();
            this.IconUrl = data.IconUrl == null ? "https://www.siteextensions.net/Content/Images/packageDefaultIcon-50x50.png" : data.IconUrl.ToString();
            this.LicenseUrl = data.LicenseUrl == null ? null : data.LicenseUrl.ToString();
            this.Authors = data.Authors.Split(new string[] { "  " }, StringSplitOptions.RemoveEmptyEntries);
            this.PublishedDateTime = data.Published;
            this.IsLatestVersion = true;
            this.DownloadCount = data.DownloadCount;
        }

        public SiteExtensionInfo(UISearchMetadata data)
        {
            this.Id = data.Identity.Id;
            this.Title = data.Identity.Id;
            this.Type = SiteExtensionType.Gallery;
            this.Summary = data.LatestPackageMetadata.Summary;
            this.Description = data.LatestPackageMetadata.Description;
            this.Version = data.Identity.Version.ToNormalizedString();
            this.ProjectUrl = data.LatestPackageMetadata.ProjectUrl == null ? null : data.LatestPackageMetadata.ProjectUrl.ToString();
            this.IconUrl = data.LatestPackageMetadata.IconUrl == null ? "https://www.siteextensions.net/Content/Images/packageDefaultIcon-50x50.png" : data.LatestPackageMetadata.IconUrl.ToString();
            this.LicenseUrl = data.LatestPackageMetadata.LicenseUrl == null ? null : data.LatestPackageMetadata.LicenseUrl.ToString();
            this.Authors = data.LatestPackageMetadata.Authors.Split(new string[] { "  " }, StringSplitOptions.RemoveEmptyEntries);
            this.PublishedDateTime = data.LatestPackageMetadata.Published;
            this.IsLatestVersion = data.Identity.Version.Equals(data.LatestPackageMetadata.Identity.Version);
            this.DownloadCount = data.LatestPackageMetadata.DownloadCount;
        }

        [JsonProperty(PropertyName = "id")]
        public string Id
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "title")]
        public string Title
        {
            get;
            set;
        }

        [JsonIgnore]
        public SiteExtensionType Type
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "summary")]
        public string Summary
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "description")]
        public string Description
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "version")]
        public string Version
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "extension_url")]
        public string ExtensionUrl
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "project_url")]
        public string ProjectUrl
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "icon_url")]
        public string IconUrl
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "license_url")]
        public string LicenseUrl
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "feed_url")]
        public string FeedUrl
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "authors")]
        public IEnumerable<string> Authors
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "published_date_time")]
        public DateTimeOffset? PublishedDateTime
        {
            get;
            set;
        }

        [JsonIgnore]
        public bool IsLatestVersion
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "download_count")]
        public int DownloadCount
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "local_is_latest_version")]
        public bool? LocalIsLatestVersion
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "local_path")]
        public string LocalPath
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "installed_date_time")]
        public DateTimeOffset? InstalledDateTime
        {
            get;
            set;
        }
    }
}
