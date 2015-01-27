#pragma warning disable
extern alias nugetcore;

namespace Kudu.Core.SiteExtensions
{
    /// <summary>
    /// 
    /// This class is only ment to have a direct reference to NuGet client dll, work-around for below issue:
    /// 
    ///     since v3 client is using MEF (reflection), there will not be any direct reference into those dependency dll,
    ///     however MSBuild would think we are not using it, and will not copy when we deploy.
    /// 
    /// </summary>
    internal class DummyReference
    {
        // NuGet.Client.dll
        NuGet.Client.V3SimpleSearchResourceProvider v3SimpleSearchResourceProvider;

        // NuGet.Client.BaseTypes.dll
        NuGet.Client.INuGetResource iNuGetResource;

        // NuGet.Client.V2.dll
        NuGet.Client.V2.V2SimpleSearchResourceProvider v2SimpleSearchResourceProvider;

        // NuGet.Client.V2.VisualStudio.dll
        NuGet.Client.V2.VisualStudio.V2UISearchResourceProvider v2UISearchResourceProvider;

        // NuGet.Client.V3.VisualStudio.dll
        NuGet.Client.V3.VisualStudio.V3UISearchResourceProvider v3UISearchResourceProvider;

        // NuGet.Client.VisualStudio.dll
        NuGet.Client.VisualStudio.UISearchResource uiSearchResource;

        // NuGetConfiguration.dll
        NuGet.Configuration.SettingValue settingValue;

        // NuGet.Core.dll
        nugetcore::NuGet.AggregateConstraintProvider aggregateConstraintProvider; 
        
        // NuGet.Data.dll
        NuGet.Data.INuGetRequestModifier iNuGetRequestModifier;

        // NuGet.Frameworks.dll
        NuGet.Frameworks.IFrameworkCompatibilityProvider iFrameworkCompatibilityProvider;

        // NuGet.Packaging.dll
        NuGet.Packaging.INuspecReader iNuspecReader;

        // NuGet.PackagingCore.dll
        NuGet.PackagingCore.IPackageReaderCore iPackageReaderCore;

        // NuGet.Resolver.dll
        NuGet.Resolver.PackageResolver packageResolver;

        // NuGet.Versioning.dll
        NuGet.Versioning.IVersionComparer iVersionComparer;
    }
}
