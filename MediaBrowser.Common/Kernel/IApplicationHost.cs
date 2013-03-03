﻿using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Updates;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Common.Kernel
{
    /// <summary>
    /// An interface to be implemented by the applications hosting a kernel
    /// </summary>
    public interface IApplicationHost
    {
        /// <summary>
        /// Restarts this instance.
        /// </summary>
        void Restart();

        /// <summary>
        /// Configures the auto run at startup.
        /// </summary>
        /// <param name="autorun">if set to <c>true</c> [autorun].</param>
        void ConfigureAutoRunAtStartup(bool autorun);

        /// <summary>
        /// Gets the application version.
        /// </summary>
        /// <value>The application version.</value>
        Version ApplicationVersion { get; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance can self update.
        /// </summary>
        /// <value><c>true</c> if this instance can self update; otherwise, <c>false</c>.</value>
        bool CanSelfUpdate { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is first run.
        /// </summary>
        /// <value><c>true</c> if this instance is first run; otherwise, <c>false</c>.</value>
        bool IsFirstRun { get; }

        /// <summary>
        /// Gets the failed assemblies.
        /// </summary>
        /// <value>The failed assemblies.</value>
        List<string> FailedAssemblies { get; }

        /// <summary>
        /// Gets all concrete types.
        /// </summary>
        /// <value>All concrete types.</value>
        Type[] AllConcreteTypes { get; }

        /// <summary>
        /// Gets the exports.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="manageLiftime">if set to <c>true</c> [manage liftime].</param>
        /// <returns>IEnumerable{``0}.</returns>
        IEnumerable<T> GetExports<T>(bool manageLiftime = true);

        /// <summary>
        /// Checks for update.
        /// </summary>
        /// <returns>Task{CheckForUpdateResult}.</returns>
        Task<CheckForUpdateResult> CheckForApplicationUpdate(CancellationToken cancellationToken, IProgress<double> progress);

        /// <summary>
        /// Updates the application.
        /// </summary>
        /// <returns>Task.</returns>
        Task UpdateApplication(PackageVersionInfo package, CancellationToken cancellationToken, IProgress<double> progress);

        /// <summary>
        /// Creates an instance of type and resolves all constructor dependancies
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>System.Object.</returns>
        object CreateInstance(Type type);

        /// <summary>
        /// Resolves this instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>``0.</returns>
        T Resolve<T>();

        /// <summary>
        /// Resolves this instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>``0.</returns>
        T TryResolve<T>();

        /// <summary>
        /// Shuts down.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Gets the plugins.
        /// </summary>
        /// <value>The plugins.</value>
        IEnumerable<IPlugin> Plugins { get; }

        /// <summary>
        /// Removes the plugin.
        /// </summary>
        /// <param name="plugin">The plugin.</param>
        void RemovePlugin(IPlugin plugin);
    }
}
