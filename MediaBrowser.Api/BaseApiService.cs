﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using ServiceStack.Common.Web;
using ServiceStack.ServiceHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Class BaseApiService
    /// </summary>
    [RequestFilter]
    public class BaseApiService : IHasResultFactory, IRestfulService
    {
        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        /// <value>The logger.</value>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Gets or sets the HTTP result factory.
        /// </summary>
        /// <value>The HTTP result factory.</value>
        public IHttpResultFactory ResultFactory { get; set; }

        /// <summary>
        /// Gets or sets the request context.
        /// </summary>
        /// <value>The request context.</value>
        public IRequestContext RequestContext { get; set; }

        /// <summary>
        /// To the optimized result.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="result">The result.</param>
        /// <returns>System.Object.</returns>
        protected object ToOptimizedResult<T>(T result)
            where T : class
        {
            return ResultFactory.GetOptimizedResult(RequestContext, result);
        }

        /// <summary>
        /// To the optimized result using cache.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="lastDateModified">The last date modified.</param>
        /// <param name="cacheDuration">Duration of the cache.</param>
        /// <param name="factoryFn">The factory fn.</param>
        /// <returns>System.Object.</returns>
        /// <exception cref="System.ArgumentNullException">cacheKey</exception>
        protected object ToOptimizedResultUsingCache<T>(Guid cacheKey, DateTime lastDateModified, TimeSpan? cacheDuration, Func<T> factoryFn)
               where T : class
        {
            return ResultFactory.GetOptimizedResultUsingCache(RequestContext, cacheKey, lastDateModified, cacheDuration, factoryFn);
        }

        /// <summary>
        /// To the cached result.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="lastDateModified">The last date modified.</param>
        /// <param name="cacheDuration">Duration of the cache.</param>
        /// <param name="factoryFn">The factory fn.</param>
        /// <param name="contentType">Type of the content.</param>
        /// <returns>System.Object.</returns>
        /// <exception cref="System.ArgumentNullException">cacheKey</exception>
        protected object ToCachedResult<T>(Guid cacheKey, DateTime lastDateModified, TimeSpan? cacheDuration, Func<T> factoryFn, string contentType)
          where T : class
        {
            return ResultFactory.GetCachedResult(RequestContext, cacheKey, lastDateModified, cacheDuration, factoryFn, contentType);
        }

        /// <summary>
        /// To the static file result.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>System.Object.</returns>
        protected object ToStaticFileResult(string path)
        {
            return ResultFactory.GetStaticFileResult(RequestContext, path);
        }

        private readonly char[] _dashReplaceChars = new[] { '?', '/' };
        private const char SlugChar = '-';

        protected Task<Artist> GetArtist(string name, ILibraryManager libraryManager)
        {
            return libraryManager.GetArtist(DeSlugArtistName(name, libraryManager));
        }

        protected Task<Studio> GetStudio(string name, ILibraryManager libraryManager)
        {
            return libraryManager.GetStudio(DeSlugStudioName(name, libraryManager));
        }

        protected Task<Genre> GetGenre(string name, ILibraryManager libraryManager)
        {
            return libraryManager.GetGenre(DeSlugGenreName(name, libraryManager));
        }

        protected Task<MusicGenre> GetMusicGenre(string name, ILibraryManager libraryManager)
        {
            return libraryManager.GetMusicGenre(DeSlugGenreName(name, libraryManager));
        }

        protected Task<GameGenre> GetGameGenre(string name, ILibraryManager libraryManager)
        {
            return libraryManager.GetGameGenre(DeSlugGenreName(name, libraryManager));
        }
        
        protected Task<Person> GetPerson(string name, ILibraryManager libraryManager)
        {
            return libraryManager.GetPerson(DeSlugPersonName(name, libraryManager));
        }

        /// <summary>
        /// Deslugs an artist name by finding the correct entry in the library
        /// </summary>
        /// <param name="name"></param>
        /// <param name="libraryManager"></param>
        /// <returns></returns>
        protected string DeSlugArtistName(string name, ILibraryManager libraryManager)
        {
            if (name.IndexOf(SlugChar) == -1)
            {
                return name;
            }
            
            return libraryManager.RootFolder.RecursiveChildren
                .OfType<Audio>()
                .SelectMany(i => new[] { i.Artist, i.AlbumArtist })
                .Where(i => !string.IsNullOrEmpty(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(i =>
                {
                    i = _dashReplaceChars.Aggregate(i, (current, c) => current.Replace(c, SlugChar));

                    return string.Equals(i, name, StringComparison.OrdinalIgnoreCase);

                }) ?? name;
        }

        /// <summary>
        /// Deslugs a genre name by finding the correct entry in the library
        /// </summary>
        protected string DeSlugGenreName(string name, ILibraryManager libraryManager)
        {
            if (name.IndexOf(SlugChar) == -1)
            {
                return name;
            }

            return libraryManager.RootFolder.RecursiveChildren
                .SelectMany(i => i.Genres)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(i =>
                {
                    i = _dashReplaceChars.Aggregate(i, (current, c) => current.Replace(c, SlugChar));

                    return string.Equals(i, name, StringComparison.OrdinalIgnoreCase);

                }) ?? name;
        }

        /// <summary>
        /// Deslugs a studio name by finding the correct entry in the library
        /// </summary>
        protected string DeSlugStudioName(string name, ILibraryManager libraryManager)
        {
            if (name.IndexOf(SlugChar) == -1)
            {
                return name;
            }

            return libraryManager.RootFolder.RecursiveChildren
                .SelectMany(i => i.Studios)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(i =>
                {
                    i = _dashReplaceChars.Aggregate(i, (current, c) => current.Replace(c, SlugChar));

                    return string.Equals(i, name, StringComparison.OrdinalIgnoreCase);

                }) ?? name;
        }

        /// <summary>
        /// Deslugs a person name by finding the correct entry in the library
        /// </summary>
        protected string DeSlugPersonName(string name, ILibraryManager libraryManager)
        {
            if (name.IndexOf(SlugChar) == -1)
            {
                return name;
            }

            return libraryManager.RootFolder.RecursiveChildren
                .SelectMany(i => i.People)
                .Select(i => i.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(i =>
                {
                    i = _dashReplaceChars.Aggregate(i, (current, c) => current.Replace(c, SlugChar));

                    return string.Equals(i, name, StringComparison.OrdinalIgnoreCase);

                }) ?? name;
        }
    }

    /// <summary>
    /// Class RequestFilterAttribute
    /// </summary>
    public class RequestFilterAttribute : Attribute, IHasRequestFilter
    {
        //This property will be resolved by the IoC container
        /// <summary>
        /// Gets or sets the user manager.
        /// </summary>
        /// <value>The user manager.</value>
        public IUserManager UserManager { get; set; }

        public ISessionManager SessionManager { get; set; }

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        /// <value>The logger.</value>
        public ILogger Logger { get; set; }

        /// <summary>
        /// The request filter is executed before the service.
        /// </summary>
        /// <param name="request">The http request wrapper</param>
        /// <param name="response">The http response wrapper</param>
        /// <param name="requestDto">The request DTO</param>
        public void RequestFilter(IHttpRequest request, IHttpResponse response, object requestDto)
        {
            //This code is executed before the service

            var auth = GetAuthorization(request);

            if (auth != null)
            {
                User user = null;

                if (auth.ContainsKey("UserId"))
                {
                    var userId = auth["UserId"];

                    if (!string.IsNullOrEmpty(userId))
                    {
                        user = UserManager.GetUserById(new Guid(userId));
                    }
                }

                var deviceId = auth["DeviceId"];
                var device = auth["Device"];
                var client = auth["Client"];

                if (!string.IsNullOrEmpty(client) && !string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(device))
                {
                    SessionManager.LogConnectionActivity(client, deviceId, device, user);
                }
            }
        }

        /// <summary>
        /// Gets the auth.
        /// </summary>
        /// <param name="httpReq">The HTTP req.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        public static Dictionary<string, string> GetAuthorization(IHttpRequest httpReq)
        {
            var auth = httpReq.Headers[HttpHeaders.Authorization];

            return GetAuthorization(auth);
        }

        /// <summary>
        /// Gets the authorization.
        /// </summary>
        /// <param name="httpReq">The HTTP req.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        public static Dictionary<string, string> GetAuthorization(IRequestContext httpReq)
        {
            var auth = httpReq.GetHeader("Authorization");

            return GetAuthorization(auth);
        }

        /// <summary>
        /// Gets the authorization.
        /// </summary>
        /// <param name="authorizationHeader">The authorization header.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        private static Dictionary<string, string> GetAuthorization(string authorizationHeader)
        {
            if (authorizationHeader == null) return null;

            var parts = authorizationHeader.Split(' ');

            // There should be at least to parts
            if (parts.Length < 2) return null;

            // It has to be a digest request
            if (!string.Equals(parts[0], "MediaBrowser", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Remove uptil the first space
            authorizationHeader = authorizationHeader.Substring(authorizationHeader.IndexOf(' '));
            parts = authorizationHeader.Split(',');

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in parts)
            {
                var param = item.Trim().Split(new[] { '=' }, 2);
                result.Add(param[0], param[1].Trim(new[] { '"' }));
            }

            return result;
        }

        /// <summary>
        /// A new shallow copy of this filter is used on every request.
        /// </summary>
        /// <returns>IHasRequestFilter.</returns>
        public IHasRequestFilter Copy()
        {
            return this;
        }

        /// <summary>
        /// Order in which Request Filters are executed.
        /// &lt;0 Executed before global request filters
        /// &gt;0 Executed after global request filters
        /// </summary>
        /// <value>The priority.</value>
        public int Priority
        {
            get { return 0; }
        }
    }
}
