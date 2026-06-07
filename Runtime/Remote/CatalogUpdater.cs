using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Addressables catalog maintenance — the single responsibility split out of the download
    /// orchestration (<see cref="RemoteContentUpdater"/>). Applies catalog updates <b>before</b> any
    /// sizing or download (so they see the new bundles), skips <c>UpdateCatalogs</c> entirely when
    /// nothing changed, and can reset the catalog-hash cache so an aborted run resumes next launch.
    /// </summary>
    public static class CatalogUpdater
    {
        /// <summary>
        /// Check for catalog updates and apply any that exist. A failed check (e.g. offline) is
        /// non-fatal — cached content can still be used. Returns the catalog ids that were updated.
        /// </summary>
        public static async UniTask<List<string>> CheckAndUpdateCatalogsAsync(CancellationToken ct = default)
        {
            var updatedCatalogIds = new List<string>();

            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            try
            {
                await checkHandle.ToUniTask(cancellationToken: ct);
                if (checkHandle.Status == AsyncOperationStatus.Succeeded && checkHandle.Result != null)
                    updatedCatalogIds.AddRange(checkHandle.Result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AddressablesToolkit] Catalog update check failed: {e.Message}");
            }
            finally
            {
                if (checkHandle.IsValid()) Addressables.Release(checkHandle);
            }

            if (updatedCatalogIds.Count == 0)
                return updatedCatalogIds;

            var updateHandle = Addressables.UpdateCatalogs(updatedCatalogIds, false);
            try
            {
                await updateHandle.ToUniTask(cancellationToken: ct);
            }
            finally
            {
                if (updateHandle.IsValid()) Addressables.Release(updateHandle);
            }

            return updatedCatalogIds;
        }

        /// <summary>
        /// Delete only the catalog-hash cache so an aborted/partial download resumes on the next
        /// launch while the bundle cache is preserved. Call on cancel or on <c>Application.quitting</c>.
        /// </summary>
        public static void ClearCatalogCacheForResume()
        {
            try
            {
                var catalogCacheDir = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
                if (Directory.Exists(catalogCacheDir))
                    Directory.Delete(catalogCacheDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AddressablesToolkit] Could not clear catalog cache for resume: {e.Message}");
            }
        }
    }
}
