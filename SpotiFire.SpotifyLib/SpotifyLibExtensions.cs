﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;

namespace SpotiFire
{
    public static class SessionExtensions
    {
        public static void Save(this Image image, string location)
        {
            image.GetImage().Save(location);
        }

        public static Search Search(this Session session,
            string query,
            int trackOffset, int trackCount,
            int albumOffset, int albumCount,
            int artistOffset, int artistCount,
            int playlistOffset, int playlistCount,
            SearchType type)
        {
            return SpotiFire.Search.Create(session, query, trackOffset, trackCount, albumOffset, albumCount, artistOffset, artistCount, playlistOffset, playlistCount, type);
        }

        public static Search SearchTracks(this Session session, string query, int trackOffset, int trackCount)
        {
            return Search(session, query, trackOffset, trackCount, 0, 0, 0, 0, 0, 0, SearchType.Standard);
        }
        public static Search SearchAlbums(this Session session, string query, int albumOffset, int albumCount)
        {
            return Search(session, query, 0, 0, albumOffset, albumCount, 0, 0, 0, 0, SearchType.Standard);
        }
        public static Search SearchArtists(this Session session, string query, int artistOffset, int artistCount)
        {
            return Search(session, query, 0, 0, 0, 0, artistOffset, artistCount, 0, 0, SearchType.Standard);
        }
        public static Search SearchPlaylist(this Session session, string query, int playlistOffset, int playlistCount)
        {
            return Search(session, query, 0, 0, 0, 0, 0, 0, playlistOffset, playlistCount, SearchType.Standard);
        }

        private readonly static List<Tuple<IAsyncLoaded, TaskCompletionSource<IAsyncLoaded>>> waiting = new List<Tuple<IAsyncLoaded, TaskCompletionSource<IAsyncLoaded>>>();

        private readonly static Timer _timer = new Timer(20);

        static SessionExtensions()
        {
            _timer.Elapsed += (sender, args) => OnTimerTick(null);
        }

        private static void OnTimerTick(object state)
        {
            lock (waiting)
            {
                if (waiting.Any())
                {
                    for (int i = waiting.Count - 1; i >= 0; i--)
                    {
                        Tuple<IAsyncLoaded, TaskCompletionSource<IAsyncLoaded>> t = waiting[i];
                        if (t.Item1.IsLoaded && t.Item1.IsReady)
                        {
                            t.Item2.TrySetResult(t.Item1);
                            waiting.RemoveAt(i);
                        }
                    }
                }
                if (!waiting.Any())
                {
                    lock (_timer) {
                        // stop the time as long as it's not needed
                        _timer.Stop();
                    }
                }
            }
        }

        // Load made a task
        private static Task<IAsyncLoaded> Load(this IAsyncLoaded loadable)
        {
            TaskCompletionSource<IAsyncLoaded> tcs = new TaskCompletionSource<IAsyncLoaded>();
            if (loadable.IsLoaded && loadable.IsReady)
            {
                tcs.SetResult(loadable);
            }
            else
            {
                lock (waiting)
                {
                    waiting.Add(new Tuple<IAsyncLoaded, TaskCompletionSource<IAsyncLoaded>>(loadable, tcs));
                }
                lock (_timer) {
                    if (!_timer.Enabled) {
                        _timer.Start();
                    }
                }
            }
            return tcs.Task;
        }

        public static TaskAwaiter<Track> GetAwaiter(this Track track)
        {
            return ((IAsyncLoaded)track).Load().ContinueWith(task => (Track)task.Result).GetAwaiter();
        }

        public static TaskAwaiter<Artist> GetAwaiter(this Artist artist)
        {
            return ((IAsyncLoaded)artist).Load().ContinueWith(task => (Artist)task.Result).GetAwaiter();
        }

        public static TaskAwaiter<Album> GetAwaiter(this Album album)
        {
            return ((IAsyncLoaded)album).Load().ContinueWith(task => (Album)task.Result).GetAwaiter();
        }

        public static TaskAwaiter<Playlist> GetAwaiter(this Playlist playlist)
        {
            return ((IAsyncLoaded)playlist).Load().ContinueWith(task => (Playlist)task.Result).GetAwaiter();
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>   A Session extension method that plays a single track and notifies it's completion. </summary>
        ///
        /// <remarks>   <para>This single extension-method takes care of unloading (in case there are any other tracks
        ///             playing), loading, and starting playback of a single track. Then, when the track is complete,
        ///             it signals the task. This enables for easy programming of behaviour such as playing through an
        ///             entire playlist, or simply playing random songs non-stop. Queueing is also easily implemented
        ///             with this method. However, this method is not meant to be used in combinations with the ability
        ///             to change what track you are currently listening to. If your application (one way or another)
        ///             allows the user (or some AI) to select a new song, whilst there is one playing, you SHOULD not
        ///             use this method, as it can have un-wanted sideeffects (in the magnitude of your application
        ///             crashing and dying horribly).</para>
        ///             
        ///              <para>The inner workings of this method is implemented using the <see cref="Session.EndOfTrack"/>
        ///              event. Every time you call this method an event-handler is attatched to the <see cref="Session.EndOfTrack"/>
        ///              event. This means that if you call this method again (before the song is finished),
        ///              it will attatch a second event-handler, and then a third, and so forth. This can probably
        ///              be resolved (one way or another), but for now, this functionality is <strong><u>not supported</u></strong>.</para> </remarks>
        ///
        /// <param name="session">  The session to act on. </param>
        /// <param name="track">    The track. </param>
        ///
        /// <returns>   A task that is signalled when the track completes. </returns>
        /// 
        /// <example>
        ///          Play through an enumerable of tracks:
        ///          <code>
        ///             <code lang="cs"><![CDATA[
        /// private async Task PlayAll(Session session, IEnumerable<Track> tracks)
        /// {
        ///     foreach(var t in tracks)
        ///         await session.Play(t);
        /// }
        ///             ]]></code>
        ///          </code>
        /// </example>
        ///-------------------------------------------------------------------------------------------------
        public static Task Play(this Session session, Track track)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            SessionEventHandler handler = null;
            handler = (s, e) =>
            {
                session.EndOfTrack -= handler;
                tcs.SetResult(null);
            };

            session.PlayerUnload();
            session.EndOfTrack += handler;
            session.PlayerLoad(track);
            session.PlayerPlay();

            return tcs.Task;
        }
    }

    public static class PlaylistExtensions
    {
        public static bool AllTracksLoaded(this Playlist playlist)
        {
            return playlist.Tracks.All(t => t.IsLoaded);
        }
    }
}
