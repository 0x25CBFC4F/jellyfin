﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Providers.Movies
{
    /// <summary>
    /// Class TmdbPersonProvider
    /// </summary>
    public class TmdbPersonProvider : BaseMetadataProvider
    {
        /// <summary>
        /// The meta file name
        /// </summary>
        protected const string MetaFileName = "tmdb3.json";

        protected readonly IProviderManager ProviderManager;
        
        public TmdbPersonProvider(IJsonSerializer jsonSerializer, ILogManager logManager, IServerConfigurationManager configurationManager, IProviderManager providerManager)
            : base(logManager, configurationManager)
        {
            if (jsonSerializer == null)
            {
                throw new ArgumentNullException("jsonSerializer");
            }
            JsonSerializer = jsonSerializer;
            ProviderManager = providerManager;
        }

        /// <summary>
        /// Gets the json serializer.
        /// </summary>
        /// <value>The json serializer.</value>
        protected IJsonSerializer JsonSerializer { get; private set; }

        /// <summary>
        /// Supportses the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        public override bool Supports(BaseItem item)
        {
            return item is Person;
        }

        protected override bool RefreshOnVersionChange
        {
            get
            {
                return true;
            }
        }

        protected override string ProviderVersion
        {
            get
            {
                return "2";
            }
        }

        /// <summary>
        /// Fetches metadata and returns true or false indicating if any work that requires persistence was done
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="force">if set to <c>true</c> [force].</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{System.Boolean}.</returns>
        public override async Task<bool> FetchAsync(BaseItem item, bool force, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var person = (Person)item;

            var id = person.GetProviderId(MetadataProviders.Tmdb);

            // We don't already have an Id, need to fetch it
            if (string.IsNullOrEmpty(id))
            {
                id = await GetTmdbId(item, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(id))
            {
                await FetchInfo(person, id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Logger.Debug("TmdbPersonProvider Unable to obtain id for " + item.Name);
            }

            SetLastRefreshed(item, DateTime.UtcNow);
            return true;
        }

        /// <summary>
        /// Gets the priority.
        /// </summary>
        /// <value>The priority.</value>
        public override MetadataProviderPriority Priority
        {
            get { return MetadataProviderPriority.Second; }
        }

        /// <summary>
        /// Gets a value indicating whether [requires internet].
        /// </summary>
        /// <value><c>true</c> if [requires internet]; otherwise, <c>false</c>.</value>
        public override bool RequiresInternet
        {
            get
            {
                return true;
            }
        }

        protected readonly CultureInfo UsCulture = new CultureInfo("en-US");
        
        /// <summary>
        /// Gets the TMDB id.
        /// </summary>
        /// <param name="person">The person.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{System.String}.</returns>
        private async Task<string> GetTmdbId(BaseItem person, CancellationToken cancellationToken)
        {
            string url = string.Format(@"http://api.themoviedb.org/3/search/person?api_key={1}&query={0}", WebUtility.UrlEncode(person.Name), MovieDbProvider.ApiKey);
            PersonSearchResults searchResult = null;

            using (Stream json = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbProvider.AcceptHeader

            }).ConfigureAwait(false))
            {
                searchResult = JsonSerializer.DeserializeFromStream<PersonSearchResults>(json);
            }

            return searchResult != null && searchResult.Total_Results > 0 ? searchResult.Results[0].Id.ToString(UsCulture) : null;
        }

        /// <summary>
        /// Fetches the info.
        /// </summary>
        /// <param name="person">The person.</param>
        /// <param name="id">The id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task FetchInfo(Person person, string id, CancellationToken cancellationToken)
        {
            string url = string.Format(@"http://api.themoviedb.org/3/person/{1}?api_key={0}&append_to_response=credits,images", MovieDbProvider.ApiKey, id);
            PersonResult searchResult = null;

            using (var json = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbProvider.AcceptHeader

            }).ConfigureAwait(false))
            {
                searchResult = JsonSerializer.DeserializeFromStream<PersonResult>(json);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (searchResult != null)
            {
                ProcessInfo(person, searchResult);

                //save locally
                var memoryStream = new MemoryStream();

                JsonSerializer.SerializeToStream(searchResult, memoryStream);

                await ProviderManager.SaveToLibraryFilesystem(person, Path.Combine(person.MetaLocation, MetaFileName), memoryStream, cancellationToken);

                Logger.Debug("TmdbPersonProvider downloaded and saved information for {0}", person.Name);

                await FetchImages(person, searchResult.images, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Processes the info.
        /// </summary>
        /// <param name="person">The person.</param>
        /// <param name="searchResult">The search result.</param>
        protected void ProcessInfo(Person person, PersonResult searchResult)
        {
            person.Overview = searchResult.biography;

            DateTime date;

            if (DateTime.TryParseExact(searchResult.birthday, "yyyy-MM-dd", new CultureInfo("en-US"), DateTimeStyles.None, out date))
            {
                person.PremiereDate = date.ToUniversalTime();
            }

            if (DateTime.TryParseExact(searchResult.deathday, "yyyy-MM-dd", new CultureInfo("en-US"), DateTimeStyles.None, out date))
            {
                person.EndDate = date.ToUniversalTime();
            }

            if (!string.IsNullOrEmpty(searchResult.homepage))
            {
                person.HomePageUrl = searchResult.homepage;
            }

            if (!string.IsNullOrEmpty(searchResult.place_of_birth))
            {
                person.AddProductionLocation(searchResult.place_of_birth);
            }
            
            person.SetProviderId(MetadataProviders.Tmdb, searchResult.id.ToString(UsCulture));
        }

        /// <summary>
        /// Fetches the images.
        /// </summary>
        /// <param name="person">The person.</param>
        /// <param name="searchResult">The search result.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task FetchImages(Person person, Images searchResult, CancellationToken cancellationToken)
        {
            if (searchResult != null && searchResult.profiles.Count > 0)
            {
                //get our language
                var profile =
                    searchResult.profiles.FirstOrDefault(
                        p =>
                        !string.IsNullOrEmpty(GetIso639(p)) &&
                        GetIso639(p).Equals(ConfigurationManager.Configuration.PreferredMetadataLanguage,
                                          StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    //didn't find our language - try first null one
                    profile =
                        searchResult.profiles.FirstOrDefault(
                            p =>
                                !string.IsNullOrEmpty(GetIso639(p)) &&
                            GetIso639(p).Equals(ConfigurationManager.Configuration.PreferredMetadataLanguage,
                                              StringComparison.OrdinalIgnoreCase));

                }
                if (profile == null)
                {
                    //still nothing - just get first one
                    profile = searchResult.profiles[0];
                }
                if (profile != null && !person.HasImage(ImageType.Primary))
                {
                    var tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);

                    var img = await DownloadAndSaveImage(person, tmdbSettings.images.base_url + ConfigurationManager.Configuration.TmdbFetchedProfileSize + profile.file_path,
                                             "folder" + Path.GetExtension(profile.file_path), cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(img))
                    {
                        person.PrimaryImagePath = img;
                    }
                }
            }
        }

        private string GetIso639(Profile p)
        {
            return p.iso_639_1 == null ? string.Empty : p.iso_639_1.ToString();
        }

        /// <summary>
        /// Downloads the and save image.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="source">The source.</param>
        /// <param name="targetName">Name of the target.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{System.String}.</returns>
        private async Task<string> DownloadAndSaveImage(BaseItem item, string source, string targetName, CancellationToken cancellationToken)
        {
            if (source == null) return null;

            //download and save locally (if not already there)
            var localPath = Path.Combine(item.MetaLocation, targetName);

            using (var sourceStream = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions
            {
                Url = source,
                CancellationToken = cancellationToken

            }).ConfigureAwait(false))
            {
                await ProviderManager.SaveToLibraryFilesystem(item, localPath, sourceStream, cancellationToken).ConfigureAwait(false);

                Logger.Debug("TmdbPersonProvider downloaded and saved image for {0}", item.Name);
            }

            return localPath;
        }

        #region Result Objects
        /// <summary>
        /// Class PersonSearchResult
        /// </summary>
        protected class PersonSearchResult
        {
            /// <summary>
            /// Gets or sets a value indicating whether this <see cref="PersonSearchResult" /> is adult.
            /// </summary>
            /// <value><c>true</c> if adult; otherwise, <c>false</c>.</value>
            public bool Adult { get; set; }
            /// <summary>
            /// Gets or sets the id.
            /// </summary>
            /// <value>The id.</value>
            public int Id { get; set; }
            /// <summary>
            /// Gets or sets the name.
            /// </summary>
            /// <value>The name.</value>
            public string Name { get; set; }
            /// <summary>
            /// Gets or sets the profile_ path.
            /// </summary>
            /// <value>The profile_ path.</value>
            public string Profile_Path { get; set; }
        }

        /// <summary>
        /// Class PersonSearchResults
        /// </summary>
        protected class PersonSearchResults
        {
            /// <summary>
            /// Gets or sets the page.
            /// </summary>
            /// <value>The page.</value>
            public int Page { get; set; }
            /// <summary>
            /// Gets or sets the results.
            /// </summary>
            /// <value>The results.</value>
            public List<PersonSearchResult> Results { get; set; }
            /// <summary>
            /// Gets or sets the total_ pages.
            /// </summary>
            /// <value>The total_ pages.</value>
            public int Total_Pages { get; set; }
            /// <summary>
            /// Gets or sets the total_ results.
            /// </summary>
            /// <value>The total_ results.</value>
            public int Total_Results { get; set; }
        }

        protected class Cast
        {
            public int id { get; set; }
            public string title { get; set; }
            public string character { get; set; }
            public string original_title { get; set; }
            public string poster_path { get; set; }
            public string release_date { get; set; }
            public bool adult { get; set; }
        }

        protected class Crew
        {
            public int id { get; set; }
            public string title { get; set; }
            public string original_title { get; set; }
            public string department { get; set; }
            public string job { get; set; }
            public string poster_path { get; set; }
            public string release_date { get; set; }
            public bool adult { get; set; }
        }

        protected class Credits
        {
            public List<Cast> cast { get; set; }
            public List<Crew> crew { get; set; }
        }

        protected class Profile
        {
            public string file_path { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public object iso_639_1 { get; set; }
            public double aspect_ratio { get; set; }
        }

        protected class Images
        {
            public List<Profile> profiles { get; set; }
        }

        protected class PersonResult
        {
            public bool adult { get; set; }
            public List<object> also_known_as { get; set; }
            public string biography { get; set; }
            public string birthday { get; set; }
            public string deathday { get; set; }
            public string homepage { get; set; }
            public int id { get; set; }
            public string imdb_id { get; set; }
            public string name { get; set; }
            public string place_of_birth { get; set; }
            public double popularity { get; set; }
            public string profile_path { get; set; }
            public Credits credits { get; set; }
            public Images images { get; set; }
        }

        #endregion
    }
}
