﻿using System;

namespace MediaBrowser.Controller.Providers
{
    /// <summary>
    /// Class BaseProviderInfo
    /// </summary>
    public class BaseProviderInfo
    {
        /// <summary>
        /// Gets or sets the last refreshed.
        /// </summary>
        /// <value>The last refreshed.</value>
        public DateTime LastRefreshed { get; set; }
        /// <summary>
        /// Gets or sets the file system stamp.
        /// </summary>
        /// <value>The file system stamp.</value>
        public string FileSystemStamp { get; set; }
        /// <summary>
        /// Gets or sets the last refresh status.
        /// </summary>
        /// <value>The last refresh status.</value>
        public ProviderRefreshStatus LastRefreshStatus { get; set; }
        /// <summary>
        /// Gets or sets the provider version.
        /// </summary>
        /// <value>The provider version.</value>
        public string ProviderVersion { get; set; }
    }

    /// <summary>
    /// Enum ProviderRefreshStatus
    /// </summary>
    public enum ProviderRefreshStatus
    {
        /// <summary>
        /// The success
        /// </summary>
        Success,
        /// <summary>
        /// The failure
        /// </summary>
        Failure,
        /// <summary>
        /// The completed with errors
        /// </summary>
        CompletedWithErrors
    }
}
