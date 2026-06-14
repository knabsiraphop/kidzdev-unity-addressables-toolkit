using System;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>Outcome of a remote-content update run.</summary>
    public enum DownloadOutcome
    {
        NoUpdate,     // nothing to download (already cached / no catalog changes)
        Success,      // bundles downloaded
        Rejected,     // user declined at the confirmation gate
        Cancelled,    // a CancellationToken fired
        NotFound,     // 403 / 404 — content missing on the CDN
        NoInternet,   // DNS / connection failure
        NoDiskSpace,  // cache write failure
        Error         // anything else
    }

    /// <summary>Typed result of a remote-content update run.</summary>
    public readonly struct DownloadResult
    {
        public readonly DownloadOutcome Outcome;
        public readonly string Message;
        public readonly long Bytes;

        public bool IsSuccess => Outcome == DownloadOutcome.Success || Outcome == DownloadOutcome.NoUpdate;

        public DownloadResult(DownloadOutcome outcome, string message = null, long bytes = 0)
        {
            Outcome = outcome;
            Message = message;
            Bytes = bytes;
        }

        public static DownloadResult NoUpdate() => new(DownloadOutcome.NoUpdate);
        public static DownloadResult Success(long bytes) => new(DownloadOutcome.Success, null, bytes);
        public static DownloadResult Rejected() => new(DownloadOutcome.Rejected, "User declined the download.");
        public static DownloadResult Cancelled() => new(DownloadOutcome.Cancelled, "Download cancelled.");

        /// <summary>
        /// Best-effort classification of a failed download.
        /// </summary>
        /// <remarks>
        /// Both reference systems classified failures purely by matching exception
        /// <em>text</em> (e.g. "HTTP/1.1 403"), which is fragile across Unity versions
        /// and locales. We prefer structured signals (exception type) first and keep a
        /// deliberately narrow text fallback only because Addressables does not surface
        /// a stable status code here. Revisit if the runtime exposes typed errors.
        /// </remarks>
        public static DownloadResult FromException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return Cancelled();

            var msg = ex?.Message ?? string.Empty;

            if (Contains(msg, "403") || Contains(msg, "404") || Contains(msg, "not found"))
                return new DownloadResult(DownloadOutcome.NotFound, msg);

            if (Contains(msg, "cannot connect") || Contains(msg, "resolve host") || Contains(msg, "unable to complete"))
                return new DownloadResult(DownloadOutcome.NoInternet, msg);

            if (Contains(msg, "unable to write") || Contains(msg, "no space") || Contains(msg, "disk full"))
                return new DownloadResult(DownloadOutcome.NoDiskSpace, msg);

            return new DownloadResult(DownloadOutcome.Error, msg);
        }

        private static bool Contains(string haystack, string needle)
            => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
