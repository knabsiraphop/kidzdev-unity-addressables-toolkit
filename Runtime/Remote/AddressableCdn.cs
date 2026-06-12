using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Installs an Addressables <see cref="Addressables.WebRequestOverride"/> that rewrites
    /// catalog/bundle requests onto a versioned, platform-specific CDN base —
    /// <c>{baseUrl}/{platformFolder}/{version}/{file}</c> — failing loudly on unknown platforms
    /// rather than silently mis-targeting. Only genuinely remote requests are rewritten:
    /// local content also flows through <c>UnityWebRequest</c> on some platforms (WebGL serves
    /// every local bundle over HTTP from StreamingAssets, Android uses <c>jar:file://</c> URLs,
    /// and local groups can opt into UnityWebRequest), and redirecting those onto the CDN
    /// would break built-in content.
    /// </summary>
    public static class AddressableCdn
    {
        /// <summary>Begin rewriting Addressables web requests onto <c>{baseUrl}/{platformFolder}/{version}</c>.</summary>
        public static void Install(string baseUrl, string version, string platformFolder = null)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentException("baseUrl is required.", nameof(baseUrl));
            if (string.IsNullOrEmpty(version)) throw new ArgumentException("version is required.", nameof(version));

            platformFolder ??= GetPlatformFolder();
            var urlPrefix = $"{baseUrl.TrimEnd('/')}/{platformFolder}/{version}";

            Addressables.WebRequestOverride = request =>
            {
                // Never throw inside the override — exceptions abort the request opaquely.
                try
                {
                    var url = request.url;
                    if (string.IsNullOrEmpty(url)) return;
                    if (!IsRemoteCandidate(url)) return;
                    if (url.StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase)) return; // already targeted

                    var lastSlashIndex = url.LastIndexOf('/');
                    var fileName = lastSlashIndex >= 0 ? url.Substring(lastSlashIndex + 1) : url;
                    request.url = $"{urlPrefix}/{fileName}";
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AddressablesToolkit] WebRequestOverride failed for '{SafeUrl(request)}': {e}");
                }
            };
        }

        /// <summary>
        /// True only for URLs that can be CDN fetches. StreamingAssets-hosted content is local
        /// even when served over HTTP (WebGL), and any non-HTTP scheme (<c>file://</c>,
        /// <c>jar:file://</c>, bare paths) is a local read — rewriting either onto the CDN
        /// would hijack built-in content.
        /// </summary>
        private static bool IsRemoteCandidate(string url)
        {
            var streamingAssets = Application.streamingAssetsPath;
            if (!string.IsNullOrEmpty(streamingAssets) &&
                url.StartsWith(streamingAssets, StringComparison.OrdinalIgnoreCase))
                return false;

            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Stop rewriting requests.</summary>
        public static void Uninstall() => Addressables.WebRequestOverride = null;

        /// <summary>
        /// Map the current runtime platform to a CDN folder name. Throws on an unrecognized platform
        /// rather than silently defaulting to one.
        /// </summary>
        public static string GetPlatformFolder()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:  return "Windows";
                case RuntimePlatform.Android:        return "Android";
                case RuntimePlatform.IPhonePlayer:   return "iOS";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:      return "OSX";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:    return "Linux";
                case RuntimePlatform.WebGLPlayer:    return "WebGL";
                default:
                    throw new NotSupportedException(
                        $"[AddressablesToolkit] No CDN platform folder mapped for {Application.platform}. " +
                        "Pass platformFolder explicitly to AddressableCdn.Install.");
            }
        }

        private static string SafeUrl(UnityWebRequest request)
        {
            try { return request?.url; } catch { return "<unknown>"; }
        }
    }
}
