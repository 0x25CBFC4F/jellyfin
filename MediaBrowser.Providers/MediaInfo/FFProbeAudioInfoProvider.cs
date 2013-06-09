﻿using MediaBrowser.Common.IO;
using MediaBrowser.Common.MediaInfo;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MediaBrowser.Providers.MediaInfo
{
    /// <summary>
    /// Extracts audio information using ffprobe
    /// </summary>
    public class FFProbeAudioInfoProvider : BaseFFProbeProvider<Audio>
    {
        public FFProbeAudioInfoProvider(ILogManager logManager, IServerConfigurationManager configurationManager, IMediaEncoder mediaEncoder, IJsonSerializer jsonSerializer)
            : base(logManager, configurationManager, mediaEncoder, jsonSerializer)
        {
        }

        /// <summary>
        /// Fetches the specified audio.
        /// </summary>
        /// <param name="audio">The audio.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="data">The data.</param>
        /// <param name="isoMount">The iso mount.</param>
        /// <returns>Task.</returns>
        protected override void Fetch(Audio audio, CancellationToken cancellationToken, MediaInfoResult data, IIsoMount isoMount)
        {
            if (data.streams == null)
            {
                Logger.Error("Audio item has no streams: " + audio.Path);
                return;
            }

            audio.MediaStreams = data.streams.Select(s => GetMediaStream(s, data.format))
                .Where(i => i != null)
                .ToList();

            // Get the first audio stream
            var stream = data.streams.FirstOrDefault(s => s.codec_type.Equals("audio", StringComparison.OrdinalIgnoreCase));

            if (stream != null)
            {
                // Get duration from stream properties
                var duration = stream.duration;

                // If it's not there go into format properties
                if (string.IsNullOrEmpty(duration))
                {
                    duration = data.format.duration;
                }

                // If we got something, parse it
                if (!string.IsNullOrEmpty(duration))
                {
                    audio.RunTimeTicks = TimeSpan.FromSeconds(double.Parse(duration, UsCulture)).Ticks;
                }
            }

            if (data.format.tags != null)
            {
                FetchDataFromTags(audio, data.format.tags);
            }
        }

        /// <summary>
        /// Fetches data from the tags dictionary
        /// </summary>
        /// <param name="audio">The audio.</param>
        /// <param name="tags">The tags.</param>
        private void FetchDataFromTags(Audio audio, Dictionary<string, string> tags)
        {
            var title = GetDictionaryValue(tags, "title");

            // Only set Name if title was found in the dictionary
            if (!string.IsNullOrEmpty(title))
            {
                audio.Name = title;
            }

            var composer = GetDictionaryValue(tags, "composer");

            if (!string.IsNullOrWhiteSpace(composer))
            {
                foreach (var person in Split(composer))
                {
                    var name = person.Trim();

                    if (!string.IsNullOrEmpty(name))
                    {
                        audio.AddPerson(new PersonInfo { Name = name, Type = PersonType.Composer });
                    }
                }
            }

            audio.Album = GetDictionaryValue(tags, "album");

            audio.Artist = GetDictionaryValue(tags, "artist");

            // Several different forms of albumartist
            audio.AlbumArtist = GetDictionaryValue(tags, "albumartist") ?? GetDictionaryValue(tags, "album artist") ?? GetDictionaryValue(tags, "album_artist");

            // Track number
            audio.IndexNumber = GetDictionaryNumericValue(tags, "track");

            // Disc number
            audio.ParentIndexNumber = GetDictionaryDiscValue(tags);

            audio.Language = GetDictionaryValue(tags, "language");

            audio.ProductionYear = GetDictionaryNumericValue(tags, "date");

            // Several different forms of retaildate
            audio.PremiereDate = GetDictionaryDateTime(tags, "retaildate") ?? GetDictionaryDateTime(tags, "retail date") ?? GetDictionaryDateTime(tags, "retail_date");

            // If we don't have a ProductionYear try and get it from PremiereDate
            if (audio.PremiereDate.HasValue && !audio.ProductionYear.HasValue)
            {
                audio.ProductionYear = audio.PremiereDate.Value.ToLocalTime().Year;
            }

            FetchGenres(audio, tags);

            // There's several values in tags may or may not be present
            FetchStudios(audio, tags, "organization");
            FetchStudios(audio, tags, "ensemble");
            FetchStudios(audio, tags, "publisher");
        }

        /// <summary>
        /// Splits the specified val.
        /// </summary>
        /// <param name="val">The val.</param>
        /// <returns>System.String[][].</returns>
        private IEnumerable<string> Split(string val)
        {
            // Only use the comma as a delimeter if there are no slashes or pipes. 
            // We want to be careful not to split names that have commas in them
            var delimeter = val.IndexOf('/') == -1 && val.IndexOf('|') == -1 ? new[] { ',' } : new[] { '/', '|' };

            return val.Split(delimeter, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Gets the studios from the tags collection
        /// </summary>
        /// <param name="audio">The audio.</param>
        /// <param name="tags">The tags.</param>
        /// <param name="tagName">Name of the tag.</param>
        private void FetchStudios(Audio audio, Dictionary<string, string> tags, string tagName)
        {
            var val = GetDictionaryValue(tags, tagName);

            if (!string.IsNullOrEmpty(val))
            {
                var studios =
                    val.Split(new[] { '/', '|' }, StringSplitOptions.RemoveEmptyEntries)
                       .Where(i => !string.Equals(i, audio.Artist, StringComparison.OrdinalIgnoreCase) && !string.Equals(i, audio.AlbumArtist, StringComparison.OrdinalIgnoreCase));

                audio.Studios.Clear();

                foreach (var studio in studios)
                {
                    audio.AddStudio(studio);
                }
            }
        }

        /// <summary>
        /// Gets the genres from the tags collection
        /// </summary>
        /// <param name="audio">The audio.</param>
        /// <param name="tags">The tags.</param>
        private void FetchGenres(Audio audio, Dictionary<string, string> tags)
        {
            var val = GetDictionaryValue(tags, "genre");

            if (!string.IsNullOrEmpty(val))
            {
                audio.Genres.Clear();

                foreach (var genre in val
                    .Split(new[] { '/', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(i => !string.IsNullOrWhiteSpace(i)))
                {
                    audio.AddGenre(genre);
                }
            }
        }

        /// <summary>
        /// Gets the disc number, which is sometimes can be in the form of '1', or '1/3'
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <returns>System.Nullable{System.Int32}.</returns>
        private int? GetDictionaryDiscValue(Dictionary<string, string> tags)
        {
            var disc = GetDictionaryValue(tags, "disc");

            if (!string.IsNullOrEmpty(disc))
            {
                disc = disc.Split('/')[0];

                int num;

                if (int.TryParse(disc, out num))
                {
                    return num;
                }
            }

            return null;
        }
    }

}
