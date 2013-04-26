﻿using System.Collections;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Search;
using ServiceStack.ServiceHost;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Class GetSearchHints
    /// </summary>
    [Route("/Search/Hints", "GET")]
    [Api(Description = "Gets search hints based on a search term")]
    public class GetSearchHints : IReturn<List<SearchHintResult>>
    {
        /// <summary>
        /// Skips over a given number of items within the results. Use for paging.
        /// </summary>
        /// <value>The start index.</value>
        [ApiMember(Name = "StartIndex", Description = "Optional. The record index to start at. All items with a lower index will be dropped from the results.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? StartIndex { get; set; }

        /// <summary>
        /// The maximum number of items to return
        /// </summary>
        /// <value>The limit.</value>
        [ApiMember(Name = "Limit", Description = "Optional. The maximum number of records to return", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? Limit { get; set; }

        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        /// <value>The user id.</value>
        [ApiMember(Name = "UserId", Description = "Optional. Supply a user id to search within a user's library or omit to search all.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public Guid? UserId { get; set; }

        /// <summary>
        /// Search characters used to find items
        /// </summary>
        /// <value>The index by.</value>
        [ApiMember(Name = "SearchTerm", Description = "The search term to filter on", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string SearchTerm { get; set; }
    }

    /// <summary>
    /// Class SearchService
    /// </summary>
    public class SearchService : BaseApiService
    {
        /// <summary>
        /// The _user manager
        /// </summary>
        private readonly IUserManager _userManager;
        /// <summary>
        /// The _search engine
        /// </summary>
        private readonly ILibrarySearchEngine _searchEngine;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchService" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="searchEngine">The search engine.</param>
        /// <param name="libraryManager">The library manager.</param>
        public SearchService(IUserManager userManager, ILibrarySearchEngine searchEngine, ILibraryManager libraryManager)
        {
            _userManager = userManager;
            _searchEngine = searchEngine;
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetSearchHints request)
        {
            var result = GetSearchHintsAsync(request).Result;

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Gets the search hints async.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Task{IEnumerable{SearchHintResult}}.</returns>
        private async Task<IEnumerable<SearchHintResult>> GetSearchHintsAsync(GetSearchHints request)
        {
            IEnumerable<BaseItem> inputItems;

            if (request.UserId.HasValue)
            {
                var user = _userManager.GetUserById(request.UserId.Value);

                inputItems = user.RootFolder.GetRecursiveChildren(user);
            }
            else
            {
                inputItems = _libraryManager.RootFolder.RecursiveChildren;
            }

            var results = await _searchEngine.GetSearchHints(inputItems, request.SearchTerm).ConfigureAwait(false);

            if (request.StartIndex.HasValue)
            {
                results = results.Skip(request.StartIndex.Value);
            }

            if (request.Limit.HasValue)
            {
                results = results.Take(request.Limit.Value);
            }

            return results.Select(GetSearchHintResult);
        }

        /// <summary>
        /// Gets the search hint result.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>SearchHintResult.</returns>
        private SearchHintResult GetSearchHintResult(BaseItem item)
        {
            var result = new SearchHintResult
            {
                Name = item.Name,
                IndexNumber = item.IndexNumber,
                ParentIndexNumber = item.ParentIndexNumber,
                ItemId = DtoBuilder.GetClientItemId(item),
                Type = item.GetType().Name,
                MediaType = item.MediaType
            };

            if (item.HasImage(ImageType.Primary))
            {
                result.PrimaryImageTag = Kernel.Instance.ImageManager.GetImageCacheTag(item, ImageType.Primary, item.GetImage(ImageType.Primary));
            }

            var episode = item as Episode;

            if (episode != null)
            {
                result.Series = episode.Series.Name;
            }

            var season = item as Season;

            if (season != null)
            {
                result.Series = season.Series.Name;
            }

            var album = item as MusicAlbum;

            if (album != null)
            {
                var songs = album.Children.OfType<Audio>().ToList();

                result.Artists = songs
                    .Select(i => i.Artist)
                    .Where(i => !string.IsNullOrEmpty(i))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                result.AlbumArtist = songs.Select(i => i.AlbumArtist).FirstOrDefault(i => !string.IsNullOrEmpty(i));
            }

            var song = item as Audio;

            if (song != null)
            {
                result.Album = song.Album;
                result.AlbumArtist = song.AlbumArtist;
                result.Artists = !string.IsNullOrEmpty(song.Artist) ? new[] { song.Artist } : new string[] { };
            }

            return result;
        }
    }
}
