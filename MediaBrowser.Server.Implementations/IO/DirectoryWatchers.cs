﻿using MediaBrowser.Common.ScheduledTasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Server.Implementations.ScheduledTasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.IO
{
    /// <summary>
    /// Class DirectoryWatchers
    /// </summary>
    public class DirectoryWatchers : IDirectoryWatchers
    {
        /// <summary>
        /// The file system watchers
        /// </summary>
        private ConcurrentBag<FileSystemWatcher> _fileSystemWatchers = new ConcurrentBag<FileSystemWatcher>();
        /// <summary>
        /// The update timer
        /// </summary>
        private Timer _updateTimer;
        /// <summary>
        /// The affected paths
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _affectedPaths = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// A dynamic list of paths that should be ignored.  Added to during our own file sytem modifications.
        /// </summary>
        private readonly ConcurrentDictionary<string,string> _tempIgnoredPaths = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The timer lock
        /// </summary>
        private readonly object _timerLock = new object();

        /// <summary>
        /// Add the path to our temporary ignore list.  Use when writing to a path within our listening scope.
        /// </summary>
        /// <param name="path">The path.</param>
        public void TemporarilyIgnore(string path)
        {
            _tempIgnoredPaths[path] = path;
        }

        /// <summary>
        /// Removes the temp ignore.
        /// </summary>
        /// <param name="path">The path.</param>
        public void RemoveTempIgnore(string path)
        {
            string val;
            _tempIgnoredPaths.TryRemove(path, out val);
        }

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        /// <value>The logger.</value>
        private ILogger Logger { get; set; }

        /// <summary>
        /// Gets or sets the task manager.
        /// </summary>
        /// <value>The task manager.</value>
        private ITaskManager TaskManager { get; set; }

        private ILibraryManager LibraryManager { get; set; }
        private IServerConfigurationManager ConfigurationManager { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectoryWatchers" /> class.
        /// </summary>
        public DirectoryWatchers(ILogManager logManager, ITaskManager taskManager, ILibraryManager libraryManager, IServerConfigurationManager configurationManager)
        {
            if (taskManager == null)
            {
                throw new ArgumentNullException("taskManager");
            }

            LibraryManager = libraryManager;
            TaskManager = taskManager;
            Logger = logManager.GetLogger("DirectoryWatchers");
            ConfigurationManager = configurationManager;
        }
        
        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            LibraryManager.LibraryChanged += Instance_LibraryChanged;

            var pathsToWatch = new List<string> { LibraryManager.RootFolder.Path };

            var paths = LibraryManager.RootFolder.Children.OfType<Folder>()
                .SelectMany(f =>
                    {
                        try
                        {
                            // Accessing ResolveArgs could involve file system access
                            return f.ResolveArgs.PhysicalLocations;
                        }
                        catch (IOException)
                        {
                            return new string[] {};
                        }

                    })
                .Where(Path.IsPathRooted);

            foreach (var path in paths)
            {
                if (!ContainsParentFolder(pathsToWatch, path))
                {
                    pathsToWatch.Add(path);
                }
            }

            foreach (var path in pathsToWatch)
            {
                StartWatchingPath(path);
            }
        }

        /// <summary>
        /// Examine a list of strings assumed to be file paths to see if it contains a parent of
        /// the provided path.
        /// </summary>
        /// <param name="lst">The LST.</param>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if [contains parent folder] [the specified LST]; otherwise, <c>false</c>.</returns>
        /// <exception cref="System.ArgumentNullException">path</exception>
        private static bool ContainsParentFolder(IEnumerable<string> lst, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }

            path = path.TrimEnd(Path.DirectorySeparatorChar);

            return lst.Any(str =>
            {
                //this should be a little quicker than examining each actual parent folder...
                var compare = str.TrimEnd(Path.DirectorySeparatorChar);

                return (path.Equals(compare, StringComparison.OrdinalIgnoreCase) || (path.StartsWith(compare, StringComparison.OrdinalIgnoreCase) && path[compare.Length] == Path.DirectorySeparatorChar));
            });
        }

        /// <summary>
        /// Starts the watching path.
        /// </summary>
        /// <param name="path">The path.</param>
        private void StartWatchingPath(string path)
        {
            // Creating a FileSystemWatcher over the LAN can take hundreds of milliseconds, so wrap it in a Task to do them all in parallel
            Task.Run(() =>
            {
                var newWatcher = new FileSystemWatcher(path, "*") { IncludeSubdirectories = true, InternalBufferSize = 32767 };

                newWatcher.Created += watcher_Changed;
                newWatcher.Deleted += watcher_Changed;
                newWatcher.Renamed += watcher_Changed;
                newWatcher.Changed += watcher_Changed;

                newWatcher.Error += watcher_Error;

                try
                {
                    newWatcher.EnableRaisingEvents = true;
                    _fileSystemWatchers.Add(newWatcher);

                    Logger.Info("Watching directory " + path);
                }
                catch (IOException ex)
                {
                    Logger.ErrorException("Error watching path: {0}", ex, path);
                }
                catch (PlatformNotSupportedException ex)
                {
                    Logger.ErrorException("Error watching path: {0}", ex, path);
                }
            });
        }

        /// <summary>
        /// Stops the watching path.
        /// </summary>
        /// <param name="path">The path.</param>
        private void StopWatchingPath(string path)
        {
            var watcher = _fileSystemWatchers.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

            if (watcher != null)
            {
                DisposeWatcher(watcher);
            }
        }

        /// <summary>
        /// Disposes the watcher.
        /// </summary>
        /// <param name="watcher">The watcher.</param>
        private void DisposeWatcher(FileSystemWatcher watcher)
        {
            Logger.Info("Stopping directory watching for path {0}", watcher.Path);

            watcher.EnableRaisingEvents = false;
            watcher.Dispose();

            var watchers = _fileSystemWatchers.ToList();

            watchers.Remove(watcher);

            _fileSystemWatchers = new ConcurrentBag<FileSystemWatcher>(watchers);
        }

        /// <summary>
        /// Handles the LibraryChanged event of the Kernel
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="MediaBrowser.Controller.Library.ChildrenChangedEventArgs" /> instance containing the event data.</param>
        void Instance_LibraryChanged(object sender, ChildrenChangedEventArgs e)
        {
            if (e.Folder is AggregateFolder && e.HasAddOrRemoveChange)
            {
                if (e.ItemsRemoved != null)
                {
                    foreach (var item in e.ItemsRemoved.OfType<Folder>())
                    {
                        StopWatchingPath(item.Path);
                    }
                }
                if (e.ItemsAdded != null)
                {
                    foreach (var item in e.ItemsAdded.OfType<Folder>())
                    {
                        StartWatchingPath(item.Path);
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Error event of the watcher control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="ErrorEventArgs" /> instance containing the event data.</param>
        async void watcher_Error(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            var dw = (FileSystemWatcher) sender;

            Logger.ErrorException("Error in Directory watcher for: "+dw.Path, ex);

            if (ex.Message.Contains("network name is no longer available"))
            {
                //Network either dropped or, we are coming out of sleep and it hasn't reconnected yet - wait and retry
                Logger.Warn("Network connection lost - will retry...");
                var retries = 0;
                var success = false;
                while (!success && retries < 10)
                {
                    await Task.Delay(500).ConfigureAwait(false);

                    try
                    {
                        dw.EnableRaisingEvents = false;
                        dw.EnableRaisingEvents = true;
                        success = true;
                    }
                    catch (IOException)
                    {
                        Logger.Warn("Network still unavailable...");
                        retries++;
                    }
                }
                if (!success)
                {
                    Logger.Warn("Unable to access network. Giving up.");
                    DisposeWatcher(dw);
                }

            }
            else
            {
                if (!ex.Message.Contains("BIOS command limit"))
                {
                    Logger.Info("Attempting to re-start watcher.");

                    dw.EnableRaisingEvents = false;
                    dw.EnableRaisingEvents = true;
                }
                
            }
        }

        /// <summary>
        /// Handles the Changed event of the watcher control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FileSystemEventArgs" /> instance containing the event data.</param>
        void watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Created && e.Name == "New folder")
            {
                return;
            }
            if (_tempIgnoredPaths.ContainsKey(e.FullPath))
            {
                Logger.Info("Watcher requested to ignore change to " + e.FullPath);
                return;
            }

            Logger.Info("Watcher sees change of type " + e.ChangeType.ToString() + " to " + e.FullPath);

            //Since we're watching created, deleted and renamed we always want the parent of the item to be the affected path
            var affectedPath = e.FullPath;

            _affectedPaths.AddOrUpdate(affectedPath, affectedPath, (key, oldValue) => affectedPath);

            lock (_timerLock)
            {
                if (_updateTimer == null)
                {
                    _updateTimer = new Timer(TimerStopped, null, TimeSpan.FromSeconds(ConfigurationManager.Configuration.FileWatcherDelay), TimeSpan.FromMilliseconds(-1));
                }
                else
                {
                    _updateTimer.Change(TimeSpan.FromSeconds(ConfigurationManager.Configuration.FileWatcherDelay), TimeSpan.FromMilliseconds(-1));
                }
            }
        }

        /// <summary>
        /// Timers the stopped.
        /// </summary>
        /// <param name="stateInfo">The state info.</param>
        private async void TimerStopped(object stateInfo)
        {
            lock (_timerLock)
            {
                // Extend the timer as long as any of the paths are still being written to.
                if (_affectedPaths.Any(p => IsFileLocked(p.Key)))
                {
                    Logger.Info("Timer extended.");
                    _updateTimer.Change(TimeSpan.FromSeconds(ConfigurationManager.Configuration.FileWatcherDelay), TimeSpan.FromMilliseconds(-1));
                    return;
                }

                Logger.Info("Timer stopped.");

                _updateTimer.Dispose();
                _updateTimer = null;
            }

            var paths = _affectedPaths.Keys.ToList();
            _affectedPaths.Clear();

            await ProcessPathChanges(paths).ConfigureAwait(false);
        }

        /// <summary>
        /// Try and determine if a file is locked
        /// This is not perfect, and is subject to race conditions, so I'd rather not make this a re-usable library method.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if [is file locked] [the specified path]; otherwise, <c>false</c>.</returns>
        private bool IsFileLocked(string path)
        {
            try
            {
                var data = FileSystem.GetFileData(path);

                if (!data.HasValue || data.Value.IsDirectory)
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return false;
            }

            FileStream stream = null;

            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
            catch
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            //file is not locked
            return false;
        }

        /// <summary>
        /// Processes the path changes.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <returns>Task.</returns>
        private async Task ProcessPathChanges(List<string> paths)
        {
            var itemsToRefresh = paths.Select(Path.GetDirectoryName)
                .Select(GetAffectedBaseItem)
                .Where(item => item != null)
                .Distinct()
                .ToList();

            foreach (var p in paths) Logger.Info(p + " reports change.");

            // If the root folder changed, run the library task so the user can see it
            if (itemsToRefresh.Any(i => i is AggregateFolder))
            {
                TaskManager.CancelIfRunningAndQueue<RefreshMediaLibraryTask>();
                return;
            }

            await Task.WhenAll(itemsToRefresh.Select(i => Task.Run(async () =>
            {
                Logger.Info(i.Name + " (" + i.Path + ") will be refreshed.");
                
                try
                {
                    await i.ChangedExternally().ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    // For now swallow and log. 
                    // Research item: If an IOException occurs, the item may be in a disconnected state (media unavailable)
                    // Should we remove it from it's parent?
                    Logger.ErrorException("Error refreshing {0}", ex, i.Name);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error refreshing {0}", ex, i.Name);
                }

            }))).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the affected base item.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>BaseItem.</returns>
        private BaseItem GetAffectedBaseItem(string path)
        {
            BaseItem item = null;

            while (item == null && !string.IsNullOrEmpty(path))
            {
                item = LibraryManager.RootFolder.FindByPath(path);

                path = Path.GetDirectoryName(path);
            }

            if (item != null)
            {
                // If the item has been deleted find the first valid parent that still exists
                while (!Directory.Exists(item.Path) && !File.Exists(item.Path))
                {
                    item = item.Parent;

                    if (item == null)
                    {
                        break;
                    }
                }
            }

            return item;
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            LibraryManager.LibraryChanged -= Instance_LibraryChanged;

            FileSystemWatcher watcher;

            while (_fileSystemWatchers.TryTake(out watcher))
            {
                watcher.Changed -= watcher_Changed;
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            lock (_timerLock)
            {
                if (_updateTimer != null)
                {
                    _updateTimer.Dispose();
                    _updateTimer = null;
                }
            } 

            _affectedPaths.Clear();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            if (dispose)
            {
                Stop();
            }
        }
    }
}
