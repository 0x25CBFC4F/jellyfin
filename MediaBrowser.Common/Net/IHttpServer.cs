using System;

namespace MediaBrowser.Common.Net
{
    /// <summary>
    /// Interface IHttpServer
    /// </summary>
    public interface IHttpServer : IDisposable
    {
        /// <summary>
        /// Gets the URL prefix.
        /// </summary>
        /// <value>The URL prefix.</value>
        string UrlPrefix { get; }

        /// <summary>
        /// Starts the specified server name.
        /// </summary>
        /// <param name="urlPrefix">The URL.</param>
        void Start(string urlPrefix);

        /// <summary>
        /// Gets a value indicating whether [supports web sockets].
        /// </summary>
        /// <value><c>true</c> if [supports web sockets]; otherwise, <c>false</c>.</value>
        bool SupportsWebSockets { get; }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        void Stop();

        /// <summary>
        /// Gets or sets a value indicating whether [enable HTTP request logging].
        /// </summary>
        /// <value><c>true</c> if [enable HTTP request logging]; otherwise, <c>false</c>.</value>
        bool EnableHttpRequestLogging { get; set; }

        /// <summary>
        /// Occurs when [web socket connected].
        /// </summary>
        event EventHandler<WebSocketConnectEventArgs> WebSocketConnected;
    }
}