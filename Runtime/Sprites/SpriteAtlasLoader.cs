using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Loads individual sprites packed in a SpriteAtlas using the addressable-key
    /// convention shared by both reference systems: <c>"{atlasKey}[{spriteName}]"</c>.
    /// Sprites are ref-counted through <see cref="AssetLoader"/>, and an optional
    /// fallback renders a placeholder when a sprite is missing (a deliberate UX safety
    /// net, like drx's <c>spr_icon_missing</c>).
    /// </summary>
    public static class SpriteAtlasLoader
    {
        /// <summary>Build the <c>"{atlasKey}[{spriteName}]"</c> addressable key.</summary>
        public static string Key(string atlasKey, string spriteName) => $"{atlasKey}[{spriteName}]";

        /// <summary>Load a sprite from an atlas. Throws if the sprite is missing.</summary>
        public static UniTask<Sprite> LoadAsync(string atlasKey, string spriteName, CancellationToken ct = default)
            => AssetLoader.LoadAsync<Sprite>(Key(atlasKey, spriteName), ct);

        /// <summary>
        /// Load a sprite from an atlas, falling back to a placeholder when the requested
        /// sprite has no location. Returns null only if the fallback is also missing.
        /// </summary>
        public static async UniTask<Sprite> LoadOrFallbackAsync(
            string atlasKey, string spriteName,
            string fallbackAtlasKey, string fallbackSpriteName,
            CancellationToken ct = default)
        {
            var key = Key(atlasKey, spriteName);
            if (await AssetLocator.ExistsAsync<Sprite>(key, ct))
                return await AssetLoader.LoadAsync<Sprite>(key, ct);

            var fallbackKey = Key(fallbackAtlasKey, fallbackSpriteName);
            if (await AssetLocator.ExistsAsync<Sprite>(fallbackKey, ct))
                return await AssetLoader.LoadAsync<Sprite>(fallbackKey, ct);

            return null;
        }

        /// <summary>Release one borrow of a previously loaded atlas sprite.</summary>
        public static void Release(string atlasKey, string spriteName)
            => AssetLoader.Release<Sprite>(Key(atlasKey, spriteName));
    }
}
