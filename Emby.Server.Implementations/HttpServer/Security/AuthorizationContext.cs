#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Net;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Emby.Server.Implementations.HttpServer.Security
{
    public class AuthorizationContext : IAuthorizationContext
    {
        private readonly IAuthenticationRepository _authRepo;
        private readonly IUserManager _userManager;

        public AuthorizationContext(IAuthenticationRepository authRepo, IUserManager userManager)
        {
            _authRepo = authRepo;
            _userManager = userManager;
        }

        public AuthorizationInfo GetAuthorizationInfo(HttpContext requestContext)
        {
            if (requestContext.Request.HttpContext.Items.TryGetValue("AuthorizationInfo", out var cached))
            {
                return (AuthorizationInfo)cached!; // Cache should never contain null
            }

            return GetAuthorization(requestContext);
        }

        public AuthorizationInfo GetAuthorizationInfo(HttpRequest requestContext)
        {
            var auth = GetAuthorizationDictionary(requestContext);
            var authInfo = GetAuthorizationInfoFromDictionary(auth, requestContext.Headers, requestContext.Query);
            return authInfo;
        }

        /// <summary>
        /// Gets the authorization.
        /// </summary>
        /// <param name="httpReq">The HTTP req.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        private AuthorizationInfo GetAuthorization(HttpContext httpReq)
        {
            var auth = GetAuthorizationDictionary(httpReq);
            var authInfo = GetAuthorizationInfoFromDictionary(auth, httpReq.Request.Headers, httpReq.Request.Query);

            httpReq.Request.HttpContext.Items["AuthorizationInfo"] = authInfo;
            return authInfo;
        }

        private AuthorizationInfo GetAuthorizationInfoFromDictionary(
            in Dictionary<string, string>? auth,
            in IHeaderDictionary headers,
            in IQueryCollection queryString)
        {
            string? deviceId = null;
            string? device = null;
            string? client = null;
            string? version = null;
            string? token = null;

            if (auth != null)
            {
                auth.TryGetValue("DeviceId", out deviceId);
                auth.TryGetValue("Device", out device);
                auth.TryGetValue("Client", out client);
                auth.TryGetValue("Version", out version);
                auth.TryGetValue("Token", out token);
            }

            if (string.IsNullOrEmpty(token))
            {
                token = headers["X-Emby-Token"];
            }

            if (string.IsNullOrEmpty(token))
            {
                token = headers["X-MediaBrowser-Token"];
            }

            if (string.IsNullOrEmpty(token))
            {
                token = queryString["ApiKey"];
            }

            // TODO deprecate this query parameter.
            if (string.IsNullOrEmpty(token))
            {
                token = queryString["api_key"];
            }

            var authInfo = new AuthorizationInfo
            {
                Client = client,
                Device = device,
                DeviceId = deviceId,
                Version = version,
                Token = token,
                IsAuthenticated = false,
                HasToken = false
            };

            if (string.IsNullOrWhiteSpace(token))
            {
                // Request doesn't contain a token.
                return authInfo;
            }

            authInfo.HasToken = true;
            var result = _authRepo.Get(new AuthenticationInfoQuery
            {
                AccessToken = token
            });

            if (result.Items.Count > 0)
            {
                authInfo.IsAuthenticated = true;
            }

            var originalAuthenticationInfo = result.Items.Count > 0 ? result.Items[0] : null;

            if (originalAuthenticationInfo != null)
            {
                var updateToken = false;

                // TODO: Remove these checks for IsNullOrWhiteSpace
                if (string.IsNullOrWhiteSpace(authInfo.Client))
                {
                    authInfo.Client = originalAuthenticationInfo.AppName;
                }

                if (string.IsNullOrWhiteSpace(authInfo.DeviceId))
                {
                    authInfo.DeviceId = originalAuthenticationInfo.DeviceId;
                }

                // Temporary. TODO - allow clients to specify that the token has been shared with a casting device
                var allowTokenInfoUpdate = authInfo.Client == null || !authInfo.Client.Contains("chromecast", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(authInfo.Device))
                {
                    authInfo.Device = originalAuthenticationInfo.DeviceName;
                }
                else if (!string.Equals(authInfo.Device, originalAuthenticationInfo.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    if (allowTokenInfoUpdate)
                    {
                        updateToken = true;
                        originalAuthenticationInfo.DeviceName = authInfo.Device;
                    }
                }

                if (string.IsNullOrWhiteSpace(authInfo.Version))
                {
                    authInfo.Version = originalAuthenticationInfo.AppVersion;
                }
                else if (!string.Equals(authInfo.Version, originalAuthenticationInfo.AppVersion, StringComparison.OrdinalIgnoreCase))
                {
                    if (allowTokenInfoUpdate)
                    {
                        updateToken = true;
                        originalAuthenticationInfo.AppVersion = authInfo.Version;
                    }
                }

                if ((DateTime.UtcNow - originalAuthenticationInfo.DateLastActivity).TotalMinutes > 3)
                {
                    originalAuthenticationInfo.DateLastActivity = DateTime.UtcNow;
                    updateToken = true;
                }

                if (!originalAuthenticationInfo.UserId.Equals(Guid.Empty))
                {
                    authInfo.User = _userManager.GetUserById(originalAuthenticationInfo.UserId);

                    if (authInfo.User != null && !string.Equals(authInfo.User.Username, originalAuthenticationInfo.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        originalAuthenticationInfo.UserName = authInfo.User.Username;
                        updateToken = true;
                    }

                    authInfo.IsApiKey = false;
                }
                else
                {
                    authInfo.IsApiKey = true;
                }

                if (updateToken)
                {
                    _authRepo.Update(originalAuthenticationInfo);
                }
            }

            return authInfo;
        }

        /// <summary>
        /// Gets the auth.
        /// </summary>
        /// <param name="httpReq">The HTTP req.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        private Dictionary<string, string>? GetAuthorizationDictionary(HttpContext httpReq)
        {
            var auth = httpReq.Request.Headers["X-Emby-Authorization"];

            if (string.IsNullOrEmpty(auth))
            {
                auth = httpReq.Request.Headers[HeaderNames.Authorization];
            }

            return GetAuthorization(auth.Count > 0 ? auth[0] : null);
        }

        /// <summary>
        /// Gets the auth.
        /// </summary>
        /// <param name="httpReq">The HTTP req.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        private Dictionary<string, string>? GetAuthorizationDictionary(HttpRequest httpReq)
        {
            var auth = httpReq.Headers["X-Emby-Authorization"];

            if (string.IsNullOrEmpty(auth))
            {
                auth = httpReq.Headers[HeaderNames.Authorization];
            }

            return GetAuthorization(auth.Count > 0 ? auth[0] : null);
        }

        /// <summary>
        /// Gets the authorization.
        /// </summary>
        /// <param name="authorizationHeader">The authorization header.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        private Dictionary<string, string>? GetAuthorization(ReadOnlySpan<char> authorizationHeader)
        {
            if (authorizationHeader == null)
            {
                return null;
            }

            var firstSpace = authorizationHeader.IndexOf(' ');

            // There should be at least two parts
            if (firstSpace == -1)
            {
                return null;
            }

            var name = authorizationHeader[..firstSpace];

            if (!name.Equals("MediaBrowser", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Emby", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Remove up until the first space
            authorizationHeader = authorizationHeader[(firstSpace + 1)..];
            return GetParts(authorizationHeader);
        }

        /// <summary>
        /// Get the authorization header components.
        /// </summary>
        /// <param name="authorizationHeader">The authorization header.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        public static Dictionary<string, string> GetParts(ReadOnlySpan<char> authorizationHeader)
        {
            var result = new Dictionary<string, string>();
            var escaped = false;
            int start = 0;
            string key = string.Empty;

            int i;
            for (i = 0; i < authorizationHeader.Length; i++)
            {
                var token = authorizationHeader[i];
                if (token == '"' || token == ',')
                {
                    // Applying a XOR logic to evaluate whether it is opening or closing a value
                    escaped = (!escaped) == (token == '"');
                    if (token == ',' && !escaped)
                    {
                        // Meeting a comma after a closing escape char means the value is complete
                        if (start < i)
                        {
                            result[key] = WebUtility.UrlDecode(authorizationHeader[start..i].Trim('"').ToString());
                            key = string.Empty;
                        }

                        start = i + 1;
                    }
                }
                else if (!escaped && token == '=')
                {
                    key = authorizationHeader[start.. i].ToString();
                    start = i + 1;
                }
            }

            // Add last value
            if (start < i)
            {
                result[key] = WebUtility.UrlDecode(authorizationHeader[start..i].Trim('"').ToString());
            }

            return result;
        }
    }
}
