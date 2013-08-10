﻿using MediaBrowser.Common.IO;
using MediaBrowser.Common.MediaInfo;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using ServiceStack.ServiceHost;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaBrowser.Api.Playback.Hls
{
    /// <summary>
    /// Class GetHlsVideoStream
    /// </summary>
    [Route("/Videos/{Id}/stream.m3u8", "GET")]
    [Api(Description = "Gets a video stream using HTTP live streaming.")]
    public class GetHlsVideoStream : VideoStreamRequest
    {

    }

    /// <summary>
    /// Class GetHlsVideoSegment
    /// </summary>
    [Route("/Videos/{Id}/segments/{SegmentId}/stream.ts", "GET")]
    [Api(Description = "Gets an Http live streaming segment file. Internal use only.")]
    public class GetHlsVideoSegment
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the segment id.
        /// </summary>
        /// <value>The segment id.</value>
        public string SegmentId { get; set; }
    }

    /// <summary>
    /// Class VideoHlsService
    /// </summary>
    public class VideoHlsService : BaseHlsService
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseStreamingService" /> class.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="isoManager">The iso manager.</param>
        /// <param name="mediaEncoder">The media encoder.</param>
        public VideoHlsService(IServerApplicationPaths appPaths, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder)
            : base(appPaths, userManager, libraryManager, isoManager, mediaEncoder)
        {
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetHlsVideoSegment request)
        {
            foreach (var playlist in Directory.EnumerateFiles(ApplicationPaths.EncodedMediaCachePath, "*.m3u8").ToList())
            {
                ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlist, TranscodingJobType.Hls);

                // Avoid implicitly captured closure
                var playlist1 = playlist;

                Task.Run(async () =>
                {
                    // This is an arbitrary time period corresponding to when the request completes.
                    await Task.Delay(30000).ConfigureAwait(false);

                    ApiEntryPoint.Instance.OnTranscodeEndRequest(playlist1, TranscodingJobType.Hls);
                });
            }
            
            var file = SegmentFilePrefix + request.SegmentId + Path.GetExtension(RequestContext.PathInfo);

            file = Path.Combine(ApplicationPaths.EncodedMediaCachePath, file);

            return ResultFactory.GetStaticFileResult(RequestContext, file);
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetHlsVideoStream request)
        {
            return ProcessRequest(request);
        }

        /// <summary>
        /// Gets the audio arguments.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        protected override string GetAudioArguments(StreamState state)
        {
            var codec = GetAudioCodec(state.Request);

            if (codec.Equals("copy", StringComparison.OrdinalIgnoreCase))
            {
                return "-codec:a:0 copy";
            }

            var args = "-codec:a:0 " + codec;

            if (state.AudioStream != null)
            {
                if (string.Equals(codec, "aac", StringComparison.OrdinalIgnoreCase))
                {
                    args += " -strict experimental";
                }
                
                var channels = GetNumAudioChannelsParam(state.Request, state.AudioStream);

                if (channels.HasValue)
                {
                    args += " -ac " + channels.Value;
                }

                if (state.Request.AudioSampleRate.HasValue)
                {
                    args += " -ar " + state.Request.AudioSampleRate.Value;
                }

                if (state.Request.AudioBitRate.HasValue)
                {
                    args += " -ab " + state.Request.AudioBitRate.Value;
                }

                var volParam = string.Empty;

                // Boost volume to 200% when downsampling from 6ch to 2ch
                if (channels.HasValue && channels.Value <= 2 && state.AudioStream.Channels.HasValue && state.AudioStream.Channels.Value > 5)
                {
                    volParam = ",volume=2.000000";
                }
                
                args += string.Format(" -af \"aresample=async=1000{0}\"", volParam);

                return args;
            }

            return args;
        }

        /// <summary>
        /// Gets the video arguments.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="performSubtitleConversion">if set to <c>true</c> [perform subtitle conversion].</param>
        /// <returns>System.String.</returns>
        protected override string GetVideoArguments(StreamState state, bool performSubtitleConversion)
        {
            var codec = GetVideoCodec(state.VideoRequest);

            // See if we can save come cpu cycles by avoiding encoding
            if (codec.Equals("copy", StringComparison.OrdinalIgnoreCase))
            {
                return IsH264(state.VideoStream) ? "-codec:v:0 copy -bsf h264_mp4toannexb" : "-codec:v:0 copy";
            }

            const string keyFrameArg = " -force_key_frames expr:if(isnan(prev_forced_t),gte(t,0),gte(t,prev_forced_t+5))";

            var args = "-codec:v:0 " + codec + " -preset superfast" + keyFrameArg;

            if (state.VideoRequest.VideoBitRate.HasValue)
            {
                // Make sure we don't request a bitrate higher than the source
                var currentBitrate = state.VideoStream == null ? state.VideoRequest.VideoBitRate.Value : state.VideoStream.BitRate ?? state.VideoRequest.VideoBitRate.Value;

                var bitrate = Math.Min(currentBitrate, state.VideoRequest.VideoBitRate.Value);

                args += string.Format(" -b:v {0}", bitrate);
            }
            
            // Add resolution params, if specified
            if (state.VideoRequest.Width.HasValue || state.VideoRequest.Height.HasValue || state.VideoRequest.MaxHeight.HasValue || state.VideoRequest.MaxWidth.HasValue)
            {
                args += GetOutputSizeParam(state, codec, performSubtitleConversion);
            }

            // Get the output framerate based on the FrameRate param
            var framerate = state.VideoRequest.Framerate ?? 0;

            // We have to supply a framerate for hls, so if it's null, account for that here
            if (framerate.Equals(0))
            {
                framerate = state.VideoStream.AverageFrameRate ?? 0;
            }
            if (framerate.Equals(0))
            {
                framerate = state.VideoStream.RealFrameRate ?? 0;
            }
            if (framerate.Equals(0))
            {
                framerate = 23.976;
            }

            framerate = Math.Round(framerate);

            args += string.Format(" -r {0}", framerate);

            args += " -vsync vfr";

            if (!string.IsNullOrEmpty(state.VideoRequest.Profile))
            {
                args += " -profile:v " + state.VideoRequest.Profile;
            }

            if (!string.IsNullOrEmpty(state.VideoRequest.Level))
            {
                args += " -level " + state.VideoRequest.Level;
            }
            
            if (state.SubtitleStream != null)
            {
                // This is for internal graphical subs
                if (!state.SubtitleStream.IsExternal && (state.SubtitleStream.Codec.IndexOf("pgs", StringComparison.OrdinalIgnoreCase) != -1 || state.SubtitleStream.Codec.IndexOf("dvd", StringComparison.OrdinalIgnoreCase) != -1))
                {
                    args += GetInternalGraphicalSubtitleParam(state, codec);
                }
            }
         
            return args;
        }

        /// <summary>
        /// Gets the segment file extension.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        protected override string GetSegmentFileExtension(StreamState state)
        {
            return ".ts";
        }
    }
}
