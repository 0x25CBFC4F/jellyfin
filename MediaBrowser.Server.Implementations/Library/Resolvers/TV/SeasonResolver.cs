﻿using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace MediaBrowser.Server.Implementations.Library.Resolvers.TV
{
    /// <summary>
    /// Class SeasonResolver
    /// </summary>
    public class SeasonResolver : FolderResolver<Season>
    {
        /// <summary>
        /// The _config
        /// </summary>
        private readonly IServerConfigurationManager _config;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeasonResolver"/> class.
        /// </summary>
        /// <param name="config">The config.</param>
        public SeasonResolver(IServerConfigurationManager config)
        {
            _config = config;
        }

        /// <summary>
        /// Resolves the specified args.
        /// </summary>
        /// <param name="args">The args.</param>
        /// <returns>Season.</returns>
        protected override Season Resolve(ItemResolveArgs args)
        {
            if (args.Parent is Series && args.IsDirectory)
            {
                var season = new Season
                {
                    IndexNumber = TVUtils.GetSeasonNumberFromPath(args.Path)
                };

                if (season.IndexNumber.HasValue && season.IndexNumber.Value == 0)
                {
                    season.Name = _config.Configuration.SeasonZeroDisplayName;
                }

                return season;
            }

            return null;
        }

        /// <summary>
        /// Sets the initial item values.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="args">The args.</param>
        protected override void SetInitialItemValues(Season item, ItemResolveArgs args)
        {
            base.SetInitialItemValues(item, args);

            Season.AddMetadataFiles(args);
        }
    }
}
