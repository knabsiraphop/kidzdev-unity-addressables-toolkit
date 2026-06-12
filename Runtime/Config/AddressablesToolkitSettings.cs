using System;
using System.Collections.Generic;
using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>Where the Addressables runtime should pull content from.</summary>
    public enum ContentSource
    {
        /// <summary>Bundles ship inside the player. No CDN, catalog check, or predownload.</summary>
        Local,

        /// <summary>Bundles live on a CDN; the toolkit can override the URL, update the
        /// catalog, and predownload labels during initialization.</summary>
        Remote
    }

    /// <summary>
    /// One named CDN target (e.g. dev / staging / production). The active environment is
    /// chosen by <see cref="AddressablesToolkitSettings.ResolveEnvironmentName"/>, so the
    /// same build can be pointed at a different backend without rebuilding content.
    /// </summary>
    [Serializable]
    public sealed class RemoteEnvironment
    {
        [Tooltip("Identifier used to select this environment, e.g. dev / staging / production.")]
        public string Name = "production";

        [Tooltip("CDN base URL. The platform folder and content version are appended, " +
                 "producing {CdnBaseUrl}/{platform}/{version}/{file} — the shape AddressableCdn installs.")]
        public string CdnBaseUrl = "https://cdn.example.com/Addressables";
    }

    /// <summary>
    /// Project-wide configuration for the toolkit's initialization and remote-content flow.
    /// One asset drives <see cref="AddressablesService"/>: it has no game-specific code, so
    /// it drops into any project. Place a single instance under a <c>Resources</c> folder
    /// (default name <see cref="ResourcesPath"/>) and it is found automatically at runtime;
    /// tests/DI can supply their own via <see cref="OverrideInstance"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = ResourcesPath,
        menuName = "KidzDev/Addressables Toolkit Settings",
        order = 0)]
    public sealed class AddressablesToolkitSettings : ScriptableObject
    {
        /// <summary>Resources path (no extension) the runtime loader looks for.</summary>
        public const string ResourcesPath = "AddressablesToolkitSettings";

        [Header("Content Source")]
        [Tooltip("Local = bundles ship in the player (CDN/catalog/predownload steps are skipped). " +
                 "Remote = content is fetched from the active environment's CDN.")]
        public ContentSource contentSource = ContentSource.Local;

        [Tooltip("Remote only. When on, the toolkit installs a WebRequestOverride that rewrites " +
                 "catalog/bundle URLs onto the active environment's CDN. Leave off to use the URLs " +
                 "baked into your Addressables profile.")]
        public bool overrideRemoteUrl = false;

        [Header("Remote Environments")]
        [Tooltip("Named CDN targets. Add as many as you need (dev, staging, production, qa, ...).")]
        public List<RemoteEnvironment> environments = new()
        {
            new RemoteEnvironment { Name = "dev",        CdnBaseUrl = "https://dev-cdn.example.com/Addressables" },
            new RemoteEnvironment { Name = "staging",    CdnBaseUrl = "https://stg-cdn.example.com/Addressables" },
            new RemoteEnvironment { Name = "production", CdnBaseUrl = "https://cdn.example.com/Addressables" },
        };

        [Tooltip("Which environment to use. Overridable at runtime via the static EnvironmentOverride " +
                 "property or the ADDRESSABLES_ENV environment variable (handy for CI/editor).")]
        public string activeEnvironment = "production";

        [Tooltip("Version segment appended to the CDN URL. Empty = Application.version.")]
        public string contentVersion = "";

        [Tooltip("Platform folder segment of the CDN URL. Empty = auto-detect from the runtime platform.")]
        public string platformFolderOverride = "";

        [Header("Preload")]
        [Tooltip("Labels (or addresses) to predownload during initialization, e.g. core, ui. " +
                 "Remote only; ignored for Local content.")]
        public List<string> preloadLabels = new();

        [Header("Initialization Flow")]
        [Tooltip("Run AddressablesService.InitializeAsync automatically at launch (BeforeSceneLoad). " +
                 "Best for Local/dev; for a remote flow with a download UI, call InitializeAsync yourself " +
                 "from your loading screen so you can pass progress/confirm.")]
        public bool autoInitializeOnLaunch = false;

        [Tooltip("Remote only. Check for and apply catalog updates during initialization.")]
        public bool checkCatalogUpdates = true;

        [Tooltip("Remote only. Predownload the preload labels during initialization.")]
        public bool predownloadPreloadContent = true;

        [Header("Diagnostics")]
        [Tooltip("Log each initialization step and state transition.")]
        public bool verboseLogging = false;

        // --- Runtime accessor -------------------------------------------------

        private static AddressablesToolkitSettings _instance;

        /// <summary>
        /// The active settings asset. Loaded once from <c>Resources/<see cref="ResourcesPath"/></c>;
        /// if none exists a default in-memory instance is returned (and a warning logged) so the
        /// toolkit degrades to sensible Local defaults rather than null-crashing.
        /// </summary>
        public static AddressablesToolkitSettings Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                _instance = Resources.Load<AddressablesToolkitSettings>(ResourcesPath);
                if (_instance == null)
                {
                    Debug.LogWarning(
                        $"[AddressablesToolkit] No settings asset found at 'Resources/{ResourcesPath}'. " +
                        "Using built-in Local defaults. Create one via " +
                        "Tools > Addressables Toolkit > Settings.");
                    _instance = CreateInstance<AddressablesToolkitSettings>();
                }
                return _instance;
            }
        }

        /// <summary>Force a specific settings instance (tests, custom loaders). Pass null to reset.</summary>
        public static void OverrideInstance(AddressablesToolkitSettings settings) => _instance = settings;

        /// <summary>Clear the cached instance and environment override (play-mode restarts without domain reload).</summary>
        internal static void ResetRuntimeStatics()
        {
            _instance = null;
            EnvironmentOverride = null;
        }

        /// <summary>
        /// Code-set environment override. Wins over the serialized <see cref="activeEnvironment"/>
        /// and the <c>ADDRESSABLES_ENV</c> variable. Set before initialization (e.g. from a build
        /// flavor) to point the same player at a different backend.
        /// </summary>
        public static string EnvironmentOverride { get; set; }

        // --- Resolution helpers ----------------------------------------------

        /// <summary>
        /// The environment name to use, in priority order: code override → <c>ADDRESSABLES_ENV</c>
        /// variable → serialized <see cref="activeEnvironment"/>.
        /// </summary>
        public string ResolveEnvironmentName()
        {
            if (!string.IsNullOrEmpty(EnvironmentOverride))
                return EnvironmentOverride;

            var fromVar = Environment.GetEnvironmentVariable("ADDRESSABLES_ENV");
            if (!string.IsNullOrEmpty(fromVar))
                return fromVar;

            return activeEnvironment;
        }

        /// <summary>
        /// The active <see cref="RemoteEnvironment"/>, matched by name (case-insensitive).
        /// Falls back to the first configured environment (with a warning) if the name is unknown,
        /// or null if none are configured.
        /// </summary>
        public RemoteEnvironment ResolveEnvironment()
        {
            var name = ResolveEnvironmentName();
            if (environments != null)
            {
                foreach (var env in environments)
                    if (env != null && string.Equals(env.Name, name, StringComparison.OrdinalIgnoreCase))
                        return env;
            }

            if (environments != null && environments.Count > 0)
            {
                Debug.LogWarning(
                    $"[AddressablesToolkit] Environment '{name}' not found; falling back to " +
                    $"'{environments[0]?.Name}'.");
                return environments[0];
            }

            return null;
        }

        /// <summary>Content version segment for the CDN URL (<see cref="contentVersion"/> or app version).</summary>
        public string ResolveVersion()
            => string.IsNullOrEmpty(contentVersion) ? Application.version : contentVersion;

        /// <summary>Platform folder override, or null to let <see cref="AddressableCdn"/> auto-detect it.</summary>
        public string ResolvePlatformFolder()
            => string.IsNullOrEmpty(platformFolderOverride) ? null : platformFolderOverride;

        /// <summary>The labels to preload, as the <c>object</c> keys the remote APIs expect.</summary>
        public List<object> GetPreloadKeys()
        {
            var keys = new List<object>();
            if (preloadLabels == null)
                return keys;
            foreach (var label in preloadLabels)
                if (!string.IsNullOrEmpty(label))
                    keys.Add(label);
            return keys;
        }
    }
}
