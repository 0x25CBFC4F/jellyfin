﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.IO;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MediaBrowser.Providers.TV
{
    class FanArtTvProvider : FanartBaseProvider
    {
        protected string FanArtBaseUrl = "http://api.fanart.tv/webservice/series/{0}/{1}/xml/all/1/1";

        internal static FanArtTvProvider Current { get; private set; }

        /// <summary>
        /// Gets the HTTP client.
        /// </summary>
        /// <value>The HTTP client.</value>
        protected IHttpClient HttpClient { get; private set; }

        private readonly IProviderManager _providerManager;

        public FanArtTvProvider(IHttpClient httpClient, ILogManager logManager, IServerConfigurationManager configurationManager, IProviderManager providerManager)
            : base(logManager, configurationManager)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException("httpClient");
            }
            HttpClient = httpClient;
            _providerManager = providerManager;
            Current = this;
        }

        public override bool Supports(BaseItem item)
        {
            return item is Series;
        }

        public override ItemUpdateType ItemUpdateType
        {
            get
            {
                return ItemUpdateType.ImageUpdate;
            }
        }
        
        /// <summary>
        /// Needses the refresh internal.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="providerInfo">The provider info.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        protected override bool NeedsRefreshInternal(BaseItem item, BaseProviderInfo providerInfo)
        {
            if (string.IsNullOrEmpty(item.GetProviderId(MetadataProviders.Tvdb)))
            {
                return false;
            }

            if (!ConfigurationManager.Configuration.DownloadSeriesImages.Art &&
                !ConfigurationManager.Configuration.DownloadSeriesImages.Logo &&
                !ConfigurationManager.Configuration.DownloadSeriesImages.Thumb &&
                !ConfigurationManager.Configuration.DownloadSeriesImages.Backdrops &&
                !ConfigurationManager.Configuration.DownloadSeriesImages.Banner)
            {
                return false;
            }

            if (item.HasImage(ImageType.Art) &&
                item.HasImage(ImageType.Logo) &&
                item.HasImage(ImageType.Banner) &&
                item.HasImage(ImageType.Thumb) &&
                item.BackdropImagePaths.Count > 0)
            {
                return false;
            }

            return base.NeedsRefreshInternal(item, providerInfo);
        }

        protected override DateTime CompareDate(BaseItem item)
        {
            var id = item.GetProviderId(MetadataProviders.Tvdb);

            if (!string.IsNullOrEmpty(id))
            {
                // Process images
                var path = GetSeriesDataPath(ConfigurationManager.ApplicationPaths, id);

                var files = new DirectoryInfo(path)
                    .EnumerateFiles("*.xml", SearchOption.TopDirectoryOnly)
                    .Select(i => i.LastWriteTimeUtc)
                    .ToArray();

                if (files.Length > 0)
                {
                    return files.Max();
                }
            }

            return base.CompareDate(item);
        }

        /// <summary>
        /// Gets a value indicating whether [refresh on version change].
        /// </summary>
        /// <value><c>true</c> if [refresh on version change]; otherwise, <c>false</c>.</value>
        protected override bool RefreshOnVersionChange
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Gets the provider version.
        /// </summary>
        /// <value>The provider version.</value>
        protected override string ProviderVersion
        {
            get
            {
                return "1";
            }
        }

        /// <summary>
        /// Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="seriesId">The series id.</param>
        /// <returns>System.String.</returns>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths, string seriesId)
        {
            var seriesDataPath = Path.Combine(GetSeriesDataPath(appPaths), seriesId);

            if (!Directory.Exists(seriesDataPath))
            {
                Directory.CreateDirectory(seriesDataPath);
            }

            return seriesDataPath;
        }

        /// <summary>
        /// Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <returns>System.String.</returns>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths)
        {
            var dataPath = Path.Combine(appPaths.DataPath, "fanart-tv");

            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }

            return dataPath;
        }
        
        protected readonly CultureInfo UsCulture = new CultureInfo("en-US");
        
        public override async Task<bool> FetchAsync(BaseItem item, bool force, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BaseProviderInfo data;

            if (!item.ProviderData.TryGetValue(Id, out data))
            {
                data = new BaseProviderInfo();
                item.ProviderData[Id] = data;
            }

            var seriesId = item.GetProviderId(MetadataProviders.Tvdb);

            var seriesDataPath = GetSeriesDataPath(ConfigurationManager.ApplicationPaths, seriesId);
            var xmlPath = Path.Combine(seriesDataPath, "fanart.xml");

            // Only download the xml if it doesn't already exist. The prescan task will take care of getting updates
            if (!File.Exists(xmlPath))
            {
                await DownloadSeriesXml(seriesDataPath, seriesId, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(xmlPath))
            {
                await FetchFromXml(item, xmlPath, cancellationToken).ConfigureAwait(false);
            }

            SetLastRefreshed(item, DateTime.UtcNow);

            return true;
        }

        /// <summary>
        /// Fetches from XML.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="xmlFilePath">The XML file path.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task FetchFromXml(BaseItem item, string xmlFilePath, CancellationToken cancellationToken)
        {
            var doc = new XmlDocument();
            doc.Load(xmlFilePath);

            cancellationToken.ThrowIfCancellationRequested();

            var language = ConfigurationManager.Configuration.PreferredMetadataLanguage.ToLower();
            
            var hd = ConfigurationManager.Configuration.DownloadHDFanArt ? "hdtv" : "clear";
            if (ConfigurationManager.Configuration.DownloadSeriesImages.Logo && !item.HasImage(ImageType.Logo))
            {
                var node = doc.SelectSingleNode("//fanart/series/" + hd + "logos/" + hd + "logo[@lang = \"" + language + "\"]/@url") ??
                            doc.SelectSingleNode("//fanart/series/clearlogos/clearlogo[@lang = \"" + language + "\"]/@url") ??
                            doc.SelectSingleNode("//fanart/series/" + hd + "logos/" + hd + "logo/@url") ??
                            doc.SelectSingleNode("//fanart/series/clearlogos/clearlogo/@url");
                var path = node != null ? node.Value : null;
                if (!string.IsNullOrEmpty(path))
                {
                    await _providerManager.SaveImage(item, path, FanArtResourcePool, ImageType.Logo, null, cancellationToken)
                          .ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            hd = ConfigurationManager.Configuration.DownloadHDFanArt ? "hd" : "";
            if (ConfigurationManager.Configuration.DownloadSeriesImages.Art && !item.HasImage(ImageType.Art))
            {
                var node = doc.SelectSingleNode("//fanart/series/" + hd + "cleararts/" + hd + "clearart[@lang = \"" + language + "\"]/@url") ??
                           doc.SelectSingleNode("//fanart/series/cleararts/clearart[@lang = \"" + language + "\"]/@url") ??
                           doc.SelectSingleNode("//fanart/series/" + hd + "cleararts/" + hd + "clearart/@url") ??
                           doc.SelectSingleNode("//fanart/series/cleararts/clearart/@url");
                var path = node != null ? node.Value : null;
                if (!string.IsNullOrEmpty(path))
                {
                    await _providerManager.SaveImage(item, path, FanArtResourcePool, ImageType.Art, null, cancellationToken)
                          .ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (ConfigurationManager.Configuration.DownloadSeriesImages.Thumb && !item.HasImage(ImageType.Thumb))
            {
                var node = doc.SelectSingleNode("//fanart/series/tvthumbs/tvthumb[@lang = \"" + language + "\"]/@url") ??
                           doc.SelectSingleNode("//fanart/series/tvthumbs/tvthumb/@url");
                var path = node != null ? node.Value : null;
                if (!string.IsNullOrEmpty(path))
                {
                    await _providerManager.SaveImage(item, path, FanArtResourcePool, ImageType.Thumb, null, cancellationToken)
                          .ConfigureAwait(false);
                }
            }

            if (ConfigurationManager.Configuration.DownloadSeriesImages.Banner && !item.HasImage(ImageType.Banner))
            {
                var node = doc.SelectSingleNode("//fanart/series/tbbanners/tvbanner[@lang = \"" + language + "\"]/@url") ??
                           doc.SelectSingleNode("//fanart/series/tbbanners/tvbanner/@url");
                var path = node != null ? node.Value : null;
                if (!string.IsNullOrEmpty(path))
                {
                    await _providerManager.SaveImage(item, path, FanArtResourcePool, ImageType.Banner, null, cancellationToken)
                          .ConfigureAwait(false);
                }
            }

            if (ConfigurationManager.Configuration.DownloadMovieImages.Backdrops && item.BackdropImagePaths.Count == 0)
            {
                var nodes = doc.SelectNodes("//fanart/series/showbackgrounds//@url");

                if (nodes != null)
                {
                    var numBackdrops = item.BackdropImagePaths.Count;

                    foreach (XmlNode node in nodes)
                    {
                        var path = node.Value;

                        if (!string.IsNullOrEmpty(path))
                        {
                            await _providerManager.SaveImage(item, path, FanArtResourcePool, ImageType.Backdrop, numBackdrops, cancellationToken)
                                  .ConfigureAwait(false);

                            numBackdrops++;

                            if (item.BackdropImagePaths.Count >= ConfigurationManager.Configuration.MaxBackdrops) break;
                        }
                    }

                }
            }

        }

        /// <summary>
        /// Downloads the series XML.
        /// </summary>
        /// <param name="seriesDataPath">The series data path.</param>
        /// <param name="tvdbId">The TVDB id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task DownloadSeriesXml(string seriesDataPath, string tvdbId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url = string.Format(FanArtBaseUrl, ApiKey, tvdbId);

            var xmlPath = Path.Combine(seriesDataPath, "fanart.xml");

            using (var response = await HttpClient.Get(new HttpRequestOptions
            {
                Url = url,
                ResourcePool = FanArtResourcePool,
                CancellationToken = cancellationToken

            }).ConfigureAwait(false))
            {
                using (var xmlFileStream = new FileStream(xmlPath, FileMode.Create, FileAccess.Write, FileShare.Read, StreamDefaults.DefaultFileStreamBufferSize, FileOptions.Asynchronous))
                {
                    await response.CopyToAsync(xmlFileStream).ConfigureAwait(false);
                }
            }
        }

    }
}
