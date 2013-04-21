﻿using MediaBrowser.Model.Updates;

namespace MediaBrowser.Model.System
{
    /// <summary>
    /// Class SystemInfo
    /// </summary>
    public class SystemInfo
    {
        /// <summary>
        /// Gets or sets the version.
        /// </summary>
        /// <value>The version.</value>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance has pending restart.
        /// </summary>
        /// <value><c>true</c> if this instance has pending restart; otherwise, <c>false</c>.</value>
        public bool HasPendingRestart { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is network deployed.
        /// </summary>
        /// <value><c>true</c> if this instance is network deployed; otherwise, <c>false</c>.</value>
        public bool IsNetworkDeployed { get; set; }

        /// <summary>
        /// Gets or sets the in progress installations.
        /// </summary>
        /// <value>The in progress installations.</value>
        public InstallationInfo[] InProgressInstallations { get; set; }

        /// <summary>
        /// Gets or sets the web socket port number.
        /// </summary>
        /// <value>The web socket port number.</value>
        public int WebSocketPortNumber { get; set; }

        /// <summary>
        /// Gets or sets the completed installations.
        /// </summary>
        /// <value>The completed installations.</value>
        public InstallationInfo[] CompletedInstallations { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [supports native web socket].
        /// </summary>
        /// <value><c>true</c> if [supports native web socket]; otherwise, <c>false</c>.</value>
        public bool SupportsNativeWebSocket { get; set; }

        /// <summary>
        /// Gets or sets plugin assemblies that failed to load.
        /// </summary>
        /// <value>The failed assembly loads.</value>
        public string[] FailedPluginAssemblies { get; set; }

        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        public string Id { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemInfo"/> class.
        /// </summary>
        public SystemInfo()
        {
            InProgressInstallations = new InstallationInfo[] { };

            CompletedInstallations = new InstallationInfo[] { };

            FailedPluginAssemblies = new string[] { };
        }
    }
}
