﻿using MediaBrowser.Common.IO;
using MediaBrowser.Common.MediaInfo;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaInfo;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Providers.MediaInfo
{
    /// <summary>
    /// Provides a base class for extracting media information through ffprobe
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class BaseFFProbeProvider<T> : BaseFFMpegProvider<T>
        where T : BaseItem
    {
        protected BaseFFProbeProvider(ILogManager logManager, IServerConfigurationManager configurationManager, IMediaEncoder mediaEncoder, IJsonSerializer jsonSerializer)
            : base(logManager, configurationManager, mediaEncoder)
        {
            JsonSerializer = jsonSerializer;
        }

        protected readonly IJsonSerializer JsonSerializer;

        /// <summary>
        /// Gets the priority.
        /// </summary>
        /// <value>The priority.</value>
        public override MetadataProviderPriority Priority
        {
            get { return MetadataProviderPriority.First; }
        }

        protected readonly CultureInfo UsCulture = new CultureInfo("en-US");

        /// <summary>
        /// Gets the media info.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="isoMount">The iso mount.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{MediaInfoResult}.</returns>
        /// <exception cref="System.ArgumentNullException">inputPath
        /// or
        /// cache</exception>
        protected async Task<MediaInfoResult> GetMediaInfo(BaseItem item, IIsoMount isoMount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = InputType.AudioFile;
            var inputPath = isoMount == null ? new[] { item.Path } : new[] { isoMount.MountedPath };

            var video = item as Video;

            if (video != null)
            {
                inputPath = MediaEncoderHelpers.GetInputArgument(video, isoMount, out type);
            }

            return await MediaEncoder.GetMediaInfo(inputPath, type, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Mounts the iso if needed.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>IsoMount.</returns>
        protected virtual Task<IIsoMount> MountIsoIfNeeded(T item, CancellationToken cancellationToken)
        {
            return NullMountTaskResult;
        }

        /// <summary>
        /// Called when [pre fetch].
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="mount">The mount.</param>
        protected virtual void OnPreFetch(T item, IIsoMount mount)
        {

        }

        /// <summary>
        /// Normalizes the FF probe result.
        /// </summary>
        /// <param name="result">The result.</param>
        protected void NormalizeFFProbeResult(MediaInfoResult result)
        {
            if (result.format != null && result.format.tags != null)
            {
                result.format.tags = ConvertDictionaryToCaseInSensitive(result.format.tags);
            }

            if (result.streams != null)
            {
                // Convert all dictionaries to case insensitive
                foreach (var stream in result.streams)
                {
                    if (stream.tags != null)
                    {
                        stream.tags = ConvertDictionaryToCaseInSensitive(stream.tags);
                    }

                    if (stream.disposition != null)
                    {
                        stream.disposition = ConvertDictionaryToCaseInSensitive(stream.disposition);
                    }
                }
            }
        }

        /// <summary>
        /// Converts ffprobe stream info to our MediaStream class
        /// </summary>
        /// <param name="streamInfo">The stream info.</param>
        /// <param name="formatInfo">The format info.</param>
        /// <returns>MediaStream.</returns>
        protected MediaStream GetMediaStream(MediaStreamInfo streamInfo, MediaFormatInfo formatInfo)
        {
            var stream = new MediaStream
            {
                Codec = streamInfo.codec_name,
                Profile = streamInfo.profile,
                Level = streamInfo.level,
                Index = streamInfo.index
            };

            if (streamInfo.tags != null)
            {
                stream.Language = GetDictionaryValue(streamInfo.tags, "language");
            }

            if (streamInfo.codec_type.Equals("audio", StringComparison.OrdinalIgnoreCase))
            {
                stream.Type = MediaStreamType.Audio;

                stream.Channels = streamInfo.channels;

                if (!string.IsNullOrEmpty(streamInfo.sample_rate))
                {
                    stream.SampleRate = int.Parse(streamInfo.sample_rate, UsCulture);
                }
            }
            else if (streamInfo.codec_type.Equals("subtitle", StringComparison.OrdinalIgnoreCase))
            {
                stream.Type = MediaStreamType.Subtitle;
            }
            else if (streamInfo.codec_type.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                stream.Type = MediaStreamType.Video;

                stream.Width = streamInfo.width;
                stream.Height = streamInfo.height;
                stream.PixelFormat = streamInfo.pix_fmt;
                stream.AspectRatio = streamInfo.display_aspect_ratio;

                stream.AverageFrameRate = GetFrameRate(streamInfo.avg_frame_rate);
                stream.RealFrameRate = GetFrameRate(streamInfo.r_frame_rate);
            }
            else
            {
                return null;
            }

            // Get stream bitrate
            if (stream.Type != MediaStreamType.Subtitle)
            {
                if (!string.IsNullOrEmpty(streamInfo.bit_rate))
                {
                    stream.BitRate = int.Parse(streamInfo.bit_rate, UsCulture);
                }
                else if (formatInfo != null && !string.IsNullOrEmpty(formatInfo.bit_rate))
                {
                    // If the stream info doesn't have a bitrate get the value from the media format info
                    stream.BitRate = int.Parse(formatInfo.bit_rate, UsCulture);
                }
            }

            if (streamInfo.disposition != null)
            {
                var isDefault = GetDictionaryValue(streamInfo.disposition, "default");
                var isForced = GetDictionaryValue(streamInfo.disposition, "forced");

                stream.IsDefault = string.Equals(isDefault, "1", StringComparison.OrdinalIgnoreCase);

                stream.IsForced = string.Equals(isForced, "1", StringComparison.OrdinalIgnoreCase);
            }

            return stream;
        }

        /// <summary>
        /// Gets a frame rate from a string value in ffprobe output
        /// This could be a number or in the format of 2997/125.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>System.Nullable{System.Single}.</returns>
        private float? GetFrameRate(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var parts = value.Split('/');

                float result;

                if (parts.Length == 2)
                {
                    result = float.Parse(parts[0], UsCulture) / float.Parse(parts[1], UsCulture);
                }
                else
                {
                    result = float.Parse(parts[0], UsCulture);
                }

                return float.IsNaN(result) ? (float?)null : result;
            }

            return null;
        }

        /// <summary>
        /// Gets a string from an FFProbeResult tags dictionary
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <param name="key">The key.</param>
        /// <returns>System.String.</returns>
        protected string GetDictionaryValue(Dictionary<string, string> tags, string key)
        {
            if (tags == null)
            {
                return null;
            }

            string val;

            tags.TryGetValue(key, out val);
            return val;
        }

        /// <summary>
        /// Gets an int from an FFProbeResult tags dictionary
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <param name="key">The key.</param>
        /// <returns>System.Nullable{System.Int32}.</returns>
        protected int? GetDictionaryNumericValue(Dictionary<string, string> tags, string key)
        {
            var val = GetDictionaryValue(tags, key);

            if (!string.IsNullOrEmpty(val))
            {
                int i;

                if (int.TryParse(val, out i))
                {
                    return i;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a DateTime from an FFProbeResult tags dictionary
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <param name="key">The key.</param>
        /// <returns>System.Nullable{DateTime}.</returns>
        protected DateTime? GetDictionaryDateTime(Dictionary<string, string> tags, string key)
        {
            var val = GetDictionaryValue(tags, key);

            if (!string.IsNullOrEmpty(val))
            {
                DateTime i;

                if (DateTime.TryParse(val, out i))
                {
                    return i.ToUniversalTime();
                }
            }

            return null;
        }

        /// <summary>
        /// Converts a dictionary to case insensitive
        /// </summary>
        /// <param name="dict">The dict.</param>
        /// <returns>Dictionary{System.StringSystem.String}.</returns>
        private Dictionary<string, string> ConvertDictionaryToCaseInSensitive(Dictionary<string, string> dict)
        {
            return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
    }
}
