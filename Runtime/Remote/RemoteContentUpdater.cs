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
    /// Startup remote-content update pipeline, modeled on the resilient "V2" flow from
    /// the reference systems:
    /// <list type="number">
    ///   <item>check for catalog updates,</item>
    ///   <item><b>apply</b> them before sizing (so size/download see the new bundles),</item>
    ///   <item>size across labels,</item>
    ///   <item>confirm with the player,</item>
    ///   <item>download with aggregate progress.</item>
    /// </list>
    /// Every handle is released on all paths; one <see cref="CancellationToken"/> threads
    /// through; <see cref="ClearCatalogCacheForResume"/> lets an aborted run resume next
    /// launch while preserving the bundle cache. Failures are returned as a typed
    /// <see cref="DownloadResult"/> rather than thrown.
    /// </summary>
    public static class RemoteContentUpdater
    {
        /// <summary>Confirmation gate. Return true to proceed with the download.</summary>
        public delegate UniTask<bool> ConfirmDownload(long totalBytes);

        /// <summary>
        /// Run the full update flow over the given labels/keys.
        /// </summary>
        public static async UniTask<DownloadResult> RunAsync(
            IEnumerable<object> labels,
            IProgress<DownloadProgress> progress = null,
            ConfirmDownload confirm = null,
            CancellationToken ct = default)
        {
            if (labels == null) throw new ArgumentNullException(nameof(labels));

            try
            {
                // 1 + 2) Check for catalog updates and apply them BEFORE sizing/downloading.
                await CheckAndUpdateCatalogsAsync(ct);

                // 3) Size across labels against the now-current catalog.
                long total = 0;
                var pending = new List<(object label, long size)>();
                foreach (var label in labels)
                {
                    ct.ThrowIfCancellationRequested();
                    long size = await DownloadHelper.GetDownloadSizeAsync(label, ct);
                    if (size > 0)
                    {
                        pending.Add((label, size));
                        total += size;
                    }
                }

                if (total == 0)
                    return DownloadResult.NoUpdate();

                // 4) Confirm.
                if (confirm != null && !await confirm(total))
                    return DownloadResult.Rejected();

                // 5) Download each pending label, reporting aggregate progress.
                long done = 0;
                foreach (var (label, size) in pending)
                {
                    ct.ThrowIfCancellationRequested();
                    await DownloadHelper.DownloadAsync(label, new AggregateProgress(progress, done, total), ct);
                    done += size;
                }

                progress?.Report(new DownloadProgress(1f, total, total));
                return DownloadResult.Success(total);
            }
            catch (OperationCanceledException)
            {
                return DownloadResult.Cancelled();
            }
            catch (Exception e)
            {
                return DownloadResult.FromException(e);
            }
        }

        /// <summary>
        /// Check for catalog updates and apply any that exist. Skips
        /// <c>UpdateCatalogs</c> entirely when there are none (both reference systems
        /// either applied too late or applied unconditionally). Returns the catalog ids
        /// that were updated.
        /// </summary>
        public static async UniTask<List<string>> CheckAndUpdateCatalogsAsync(CancellationToken ct = default)
        {
            var catalogs = new List<string>();

            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            try
            {
                await checkHandle.ToUniTask(cancellationToken: ct);
                if (checkHandle.Status == AsyncOperationStatus.Succeeded && checkHandle.Result != null)
                    catalogs.AddRange(checkHandle.Result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // Check failing (e.g. offline) is not fatal: cached content can still be used.
                Debug.LogWarning($"[AddressablesToolkit] Catalog update check failed: {e.Message}");
            }
            finally
            {
                if (checkHandle.IsValid()) Addressables.Release(checkHandle);
            }

            if (catalogs.Count == 0)
                return catalogs;

            var updateHandle = Addressables.UpdateCatalogs(catalogs, false);
            try
            {
                await updateHandle.ToUniTask(cancellationToken: ct);
            }
            finally
            {
                if (updateHandle.IsValid()) Addressables.Release(updateHandle);
            }

            return catalogs;
        }

        /// <summary>
        /// Delete only the catalog-hash cache directory so an aborted/partial download
        /// resumes on the next launch while the bundle cache is preserved. Call on
        /// cancel or on <c>Application.quitting</c> after a cancelled run.
        /// </summary>
        public static void ClearCatalogCacheForResume()
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AddressablesToolkit] Could not clear catalog cache for resume: {e.Message}");
            }
        }

        /// <summary>Relays a single label's progress into the overall byte total.</summary>
        private sealed class AggregateProgress : IProgress<DownloadProgress>
        {
            private readonly IProgress<DownloadProgress> _inner;
            private readonly long _alreadyDone;
            private readonly long _grandTotal;

            public AggregateProgress(IProgress<DownloadProgress> inner, long alreadyDone, long grandTotal)
            {
                _inner = inner;
                _alreadyDone = alreadyDone;
                _grandTotal = grandTotal;
            }

            public void Report(DownloadProgress p)
            {
                if (_inner == null) return;
                long done = _alreadyDone + p.DownloadedBytes;
                float percent = _grandTotal > 0 ? Mathf.Clamp01((float)done / _grandTotal) : 1f;
                _inner.Report(new DownloadProgress(percent, done, _grandTotal));
            }
        }
    }
}
