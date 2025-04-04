// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.DependencyInjection.Abstraction;
using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Core.IO.Compression.Zstandard;
using Snap.Hutao.Core.IO.Hashing;
using Snap.Hutao.Core.Threading.RateLimiting;
using Snap.Hutao.Factory.IO;
using Snap.Hutao.Factory.Progress;
using Snap.Hutao.UI.Xaml.View.Window;
using Snap.Hutao.Web.Hoyolab.Downloader;
using Snap.Hutao.Web.Hoyolab.HoyoPlay.Connect.Branch;
using Snap.Hutao.Web.Hoyolab.Takumi.Downloader.Proto;
using Snap.Hutao.Web.Response;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

namespace Snap.Hutao.Service.Game.Package.Advanced;

[ConstructorGenerated]
[Injection(InjectAs.Singleton, typeof(IGamePackageService))]
[SuppressMessage("", "CA1001")]
[SuppressMessage("", "SA1201")]
[SuppressMessage("", "SA1204")]
internal sealed partial class GamePackageService : IGamePackageService
{
    public const string HttpClientName = "SophonChunkRateLimited";

    private readonly IMemoryStreamFactory memoryStreamFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<GamePackageService> logger;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly IProgressFactory progressFactory;
    private readonly IServiceProvider serviceProvider;

    private CancellationTokenSource? operationCts;
    private TaskCompletionSource? operationTcs;

    public async ValueTask<bool> ExecuteOperationAsync(GamePackageOperationContext operationContext)
    {
        await CancelOperationAsync().ConfigureAwait(false);

        operationCts = new();
        operationTcs = new();

        ParallelOptions options = new()
        {
            CancellationToken = operationCts.Token,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        };

        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            ITaskContext taskContext = scope.ServiceProvider.GetRequiredService<ITaskContext>();

            await taskContext.SwitchToMainThreadAsync();

            GamePackageOperationWindow window = scope.ServiceProvider.GetRequiredService<GamePackageOperationWindow>();
            IProgress<GamePackageOperationReport> progress = progressFactory.CreateForMainThread<GamePackageOperationReport>(window.HandleProgressUpdate);

            await taskContext.SwitchToBackgroundAsync();

            bool result;
            using (HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName))
            {
                using (TokenBucketRateLimiter? limiter = StreamCopyRateLimiter.Create(serviceProvider))
                {
                    Func<GamePackageServiceContext, ValueTask> operation = operationContext.Kind switch
                    {
                        GamePackageOperationKind.Install => InstallAsync,
                        GamePackageOperationKind.Verify => VerifyAndRepairAsync,
                        GamePackageOperationKind.Update => UpdateAsync,
                        GamePackageOperationKind.Predownload => PredownloadAsync,
                        GamePackageOperationKind.ExtractBlk => ExtractAsync,
                        GamePackageOperationKind.ExtractExe => ExtractExeAsync,
                        _ => static context => ValueTask.FromException(HutaoException.NotSupported()),
                    };

                    try
                    {
                        GamePackageServiceContext serviceContext = new(operationContext, progress, options, httpClient, limiter);
                        await operation(serviceContext).ConfigureAwait(false);
                        result = true;
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogDebug("Operation canceled");
                        result = false;
                    }
                    catch (Exception ex)
                    {
                        logger.LogCritical(ex, "Unexpected exception while executing game package operation");
                        result = false;
                    }
                    finally
                    {
                        logger.LogDebug("Operation completed");
                        operationTcs.TrySetResult();
                    }
                }
            }

            await window.CloseTask.ConfigureAwait(false);
            return result;
        }
    }

    public async ValueTask CancelOperationAsync()
    {
        if (operationCts is null || operationTcs is null)
        {
            return;
        }

        await operationCts.CancelAsync().ConfigureAwait(false);
        await operationTcs.Task.ConfigureAwait(false);
        operationCts.Dispose();
        operationCts = null;
        operationTcs = null;
    }

    private static IEnumerable<SophonAssetOperation> GetDiffOperations(SophonDecodedBuild localDecodedBuild, SophonDecodedBuild remoteDecodedBuild)
    {
        foreach ((SophonDecodedManifest localManifest, SophonDecodedManifest remoteManifest) in localDecodedBuild.Manifests.Zip(remoteDecodedBuild.Manifests))
        {
            foreach (AssetProperty remoteAsset in remoteManifest.ManifestProto.Assets)
            {
                if (localManifest.ManifestProto.Assets.FirstOrDefault(localAsset => localAsset.AssetName.Equals(remoteAsset.AssetName, StringComparison.OrdinalIgnoreCase)) is not { } localAsset)
                {
                    yield return SophonAssetOperation.AddOrRepair(remoteManifest.UrlPrefix, remoteAsset);
                    continue;
                }

                if (localAsset.AssetHashMd5.Equals(remoteAsset.AssetHashMd5, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ImmutableArray<SophonChunk>.Builder diffChunks = ImmutableArray.CreateBuilder<SophonChunk>();
                foreach (AssetChunk chunk in remoteAsset.AssetChunks)
                {
                    if (localAsset.AssetChunks.FirstOrDefault(c => c.ChunkDecompressedHashMd5.Equals(chunk.ChunkDecompressedHashMd5, StringComparison.OrdinalIgnoreCase)) is null)
                    {
                        diffChunks.Add(new(remoteManifest.UrlPrefix, chunk));
                    }
                }

                yield return SophonAssetOperation.Modify(remoteManifest.UrlPrefix, localAsset, remoteAsset, diffChunks.ToImmutable());
            }

            foreach (AssetProperty localAsset in localManifest.ManifestProto.Assets)
            {
                if (remoteManifest.ManifestProto.Assets.FirstOrDefault(a => a.AssetName.Equals(localAsset.AssetName, StringComparison.OrdinalIgnoreCase)) is null)
                {
                    yield return SophonAssetOperation.Delete(localAsset);
                }
            }
        }
    }

    private static void InitializeDuplicatedChunkNames(GamePackageServiceContext context, IEnumerable<AssetChunk> chunks)
    {
        Debug.Assert(context.DuplicatedChunkNames.IsEmpty);
        IEnumerable<string> names = chunks
            .GroupBy(chunk => chunk.ChunkName)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .Distinct();

        foreach (string name in names)
        {
            context.DuplicatedChunkNames.TryAdd(name, default);
        }
    }

    private static async ValueTask PrivateVerifyAndRepairAsync(GamePackageServiceContext context, SophonDecodedBuild build, long totalBytes, int totalBlockCount)
    {
        context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedVerifyingIntegrity, 0, totalBlockCount, totalBytes));
        GamePackageIntegrityInfo info = await context.Operation.Asset.VerifyGamePackageIntegrityAsync(context, build).ConfigureAwait(false);

        if (info.NoConflict)
        {
            context.Progress.Report(new GamePackageOperationReport.Finish(context.Operation.Kind));
            return;
        }

        (int conflictedBlocks, long conflictedBytes) = info.GetConflictedBlockCountAndByteCount(context.Operation.GameChannelSDK);
        context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedRepairing, conflictedBlocks, conflictedBytes));

        await context.Operation.Asset.RepairGamePackageAsync(context, info).ConfigureAwait(false);

        if (Directory.Exists(context.Operation.EffectiveChunksDirectory))
        {
            Directory.Delete(context.Operation.EffectiveChunksDirectory, true);
        }

        context.Progress.Report(new GamePackageOperationReport.Finish(context.Operation.Kind, context.Operation.Kind is GamePackageOperationKind.Verify));
    }

    private static int GetUniqueTotalBlocks(ImmutableArray<SophonAssetOperation> assets)
    {
        HashSet<string> uniqueChunkNames = [];
        foreach (ref readonly SophonAssetOperation asset in assets.AsSpan())
        {
            switch (asset.Kind)
            {
                case SophonAssetOperationKind.AddOrRepair:
                    foreach (ref readonly AssetChunk chunk in CollectionsMarshal.AsSpan(asset.NewAsset.AssetChunks.ToList()))
                    {
                        uniqueChunkNames.Add(chunk.ChunkName);
                    }

                    break;
                case SophonAssetOperationKind.Modify:
                    foreach (ref readonly SophonChunk diffChunk in asset.DiffChunks.AsSpan())
                    {
                        uniqueChunkNames.Add(diffChunk.AssetChunk.ChunkName);
                    }

                    break;
            }
        }

        return uniqueChunkNames.Count;
    }

    private static int GetDownloadTotalBlocks(ImmutableArray<SophonAssetOperation> assets)
    {
        int totalBlocks = 0;
        foreach (ref readonly SophonAssetOperation asset in assets.AsSpan())
        {
            switch (asset.Kind)
            {
                case SophonAssetOperationKind.AddOrRepair:
                    totalBlocks += asset.NewAsset.AssetChunks.Count;
                    break;
                case SophonAssetOperationKind.Modify:
                    totalBlocks += asset.DiffChunks.Length;
                    break;
            }
        }

        return totalBlocks;
    }

    private static int GetInstallTotalBlocks(ImmutableArray<SophonAssetOperation> assets)
    {
        int totalBlocks = 0;
        foreach (ref readonly SophonAssetOperation asset in assets.AsSpan())
        {
            switch (asset.Kind)
            {
                case SophonAssetOperationKind.AddOrRepair or SophonAssetOperationKind.Modify:
                    totalBlocks += asset.NewAsset.AssetChunks.Count;
                    break;
            }
        }

        return totalBlocks;
    }

    private static long GetTotalBytes(ImmutableArray<SophonAssetOperation> assets)
    {
        long totalBytes = 0;
        foreach (ref readonly SophonAssetOperation diffAsset in assets.AsSpan())
        {
            switch (diffAsset.Kind)
            {
                case SophonAssetOperationKind.AddOrRepair:
                    totalBytes += diffAsset.NewAsset.AssetSize;
                    break;
                case SophonAssetOperationKind.Modify:
                    totalBytes += diffAsset.DiffChunks.Sum(c => c.AssetChunk.ChunkSizeDecompressed);
                    break;
            }
        }

        return totalBytes;
    }

    private async ValueTask VerifyAndRepairAsync(GamePackageServiceContext context)
    {
        if (await DecodeManifestsAsync(context, context.Operation.LocalBranch).ConfigureAwait(false) is not { } localBuild)
        {
            context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedDecodeManifestFailed));
            return;
        }

        await PrivateVerifyAndRepairAsync(context, localBuild, localBuild.TotalBytes, localBuild.TotalChunks).ConfigureAwait(false);
    }

    private async ValueTask InstallAsync(GamePackageServiceContext context)
    {
        if (await DecodeManifestsAsync(context, context.Operation.RemoteBranch).ConfigureAwait(false) is not { } remoteBuild)
        {
            context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedDecodeManifestFailed));
            return;
        }

        long totalBytes = remoteBuild.TotalBytes;
        if (!context.EnsureAvailableFreeSpace(totalBytes))
        {
            return;
        }

        InitializeDuplicatedChunkNames(context, remoteBuild.Manifests.SelectMany(m => m.ManifestProto.Assets.SelectMany(a => a.AssetChunks)));

        int totalBlockCount = remoteBuild.TotalChunks;
        context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedInstalling, totalBlockCount, totalBytes));

        await context.Operation.Asset.InstallAssetsAsync(context, remoteBuild).ConfigureAwait(false);
        await context.Operation.Asset.EnsureChannelSdkAsync(context).ConfigureAwait(false);

        await PrivateVerifyAndRepairAsync(context, remoteBuild, totalBytes, totalBlockCount).ConfigureAwait(false);

        if (Directory.Exists(context.Operation.EffectiveChunksDirectory))
        {
            Directory.Delete(context.Operation.EffectiveChunksDirectory, true);
        }
    }

    private async ValueTask UpdateAsync(GamePackageServiceContext context)
    {
        if (await DecodeManifestsAsync(context, context.Operation.LocalBranch).ConfigureAwait(false) is not { } localBuild ||
            await DecodeManifestsAsync(context, context.Operation.RemoteBranch).ConfigureAwait(false) is not { } remoteBuild)
        {
            context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedDecodeManifestFailed));
            return;
        }

        ImmutableArray<SophonAssetOperation> diffAssets = [.. GetDiffOperations(localBuild, remoteBuild).OrderBy(a => a.Kind)];

        int downloadTotalChunks = GetDownloadTotalBlocks(diffAssets);
        int installTotalChunks = GetInstallTotalBlocks(diffAssets);
        long totalBytes = GetTotalBytes(diffAssets);

        if (!context.EnsureAvailableFreeSpace(totalBytes))
        {
            return;
        }

        InitializeDuplicatedChunkNames(context, diffAssets.SelectMany(a => a.DiffChunks.Select(c => c.AssetChunk)));

        context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedUpdating, downloadTotalChunks, installTotalChunks, totalBytes));

        await context.Operation.Asset.UpdateDiffAssetsAsync(context, diffAssets).ConfigureAwait(false);
        await context.Operation.Asset.EnsureChannelSdkAsync(context).ConfigureAwait(false);

        await PrivateVerifyAndRepairAsync(context, remoteBuild, remoteBuild.TotalBytes, remoteBuild.TotalChunks).ConfigureAwait(false);

        context.Operation.GameFileSystem.TryUpdateConfigurationFile(context.Operation.RemoteBranch.Tag);

        if (Directory.Exists(context.Operation.EffectiveChunksDirectory))
        {
            Directory.Delete(context.Operation.EffectiveChunksDirectory, true);
        }
    }

    private async ValueTask PredownloadAsync(GamePackageServiceContext context)
    {
        if (await DecodeManifestsAsync(context, context.Operation.LocalBranch).ConfigureAwait(false) is not { } localBuild ||
            await DecodeManifestsAsync(context, context.Operation.RemoteBranch).ConfigureAwait(false) is not { } remoteBuild)
        {
            context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedDecodeManifestFailed));
            return;
        }

        ImmutableArray<SophonAssetOperation> diffAssets = [.. GetDiffOperations(localBuild, remoteBuild).OrderBy(a => a.Kind)];

        int uniqueTotalBlocks = GetUniqueTotalBlocks(diffAssets);
        int totalBlocks = GetDownloadTotalBlocks(diffAssets);
        long totalBytes = GetTotalBytes(diffAssets);

        if (!context.EnsureAvailableFreeSpace(totalBytes))
        {
            return;
        }

        context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedPredownloading, totalBlocks, 0, totalBytes));

        if (!Directory.Exists(context.Operation.GameFileSystem.GetChunksDirectory()))
        {
            Directory.CreateDirectory(context.Operation.GameFileSystem.GetChunksDirectory());
        }

        PredownloadStatus predownloadStatus = new(context.Operation.RemoteBranch.Tag, false, uniqueTotalBlocks);
        using (FileStream predownloadStatusStream = File.Create(context.Operation.GameFileSystem.GetPredownloadStatusPath()))
        {
            await JsonSerializer.SerializeAsync(predownloadStatusStream, predownloadStatus, jsonOptions).ConfigureAwait(false);
        }

        await context.Operation.Asset.PredownloadDiffAssetsAsync(context, diffAssets).ConfigureAwait(false);

        context.Progress.Report(new GamePackageOperationReport.Finish(context.Operation.Kind));

        using (FileStream predownloadStatusStream = File.Create(context.Operation.GameFileSystem.GetPredownloadStatusPath()))
        {
            predownloadStatus.Finished = true;
            await JsonSerializer.SerializeAsync(predownloadStatusStream, predownloadStatus, jsonOptions).ConfigureAwait(false);
        }
    }

    private async ValueTask<SophonDecodedBuild?> DecodeManifestsAsync(GamePackageServiceContext context, BranchWrapper branch)
    {
        CancellationToken token = context.CancellationToken;

        SophonBuild? build;
        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            ISophonClient client = scope.ServiceProvider
                .GetRequiredService<IOverseaSupportFactory<ISophonClient>>()
                .Create(context.Operation.GameFileSystem.IsOversea());

            Response<SophonBuild> response = await client.GetBuildAsync(branch, token).ConfigureAwait(false);
            if (!ResponseValidator.TryValidate(response, scope.ServiceProvider, out build))
            {
                return default!;
            }
        }

        long totalBytes = 0L;
        List<SophonDecodedManifest> decodedManifests = [];
        foreach (SophonManifest sophonManifest in build.Manifests)
        {
            bool exclude = sophonManifest.MatchingField switch
            {
                "game" => false,
                "zh-cn" => !context.Operation.GameFileSystem.Audio.Chinese,
                "en-us" => !context.Operation.GameFileSystem.Audio.English,
                "ja-jp" => !context.Operation.GameFileSystem.Audio.Japanese,
                "ko-kr" => !context.Operation.GameFileSystem.Audio.Korean,
                _ => true,
            };

            if (exclude)
            {
                continue;
            }

            totalBytes += sophonManifest.Stats.UncompressedSize;
            string manifestDownloadUrl = $"{sophonManifest.ManifestDownload.UrlPrefix}/{sophonManifest.Manifest.Id}";

            using (Stream rawManifestStream = await context.HttpClient.GetStreamAsync(manifestDownloadUrl, token).ConfigureAwait(false))
            {
                using (ZstandardDecompressionStream decompressor = new(rawManifestStream))
                {
                    using (MemoryStream inMemoryManifestStream = await memoryStreamFactory.GetStreamAsync(decompressor).ConfigureAwait(false))
                    {
                        string manifestMd5 = await Hash.ToHexStringAsync(HashAlgorithmName.MD5, inMemoryManifestStream, token).ConfigureAwait(false);
                        if (manifestMd5.Equals(sophonManifest.Manifest.Checksum, StringComparison.OrdinalIgnoreCase))
                        {
                            inMemoryManifestStream.Position = 0;
                            decodedManifests.Add(new(sophonManifest.ChunkDownload.UrlPrefix, SophonManifestProto.Parser.ParseFrom(inMemoryManifestStream)));
                        }
                    }
                }
            }
        }

        return new(totalBytes, decodedManifests);
    }

    #region Dev Only

    [GeneratedRegex(@"AssetBundles.*\.blk$", RegexOptions.IgnoreCase)]
    private static partial Regex AssetBundlesBlockRegex { get; }

    [GeneratedRegex(@"^(Yuanshen|GenshinImpact)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex GameExecutableFileRegex { get; }

    private async ValueTask ExtractAsync(GamePackageServiceContext context)
    {
        if (await DecodeManifestsAsync(context, context.Operation.LocalBranch).ConfigureAwait(false) is not { } localBuild ||
            await DecodeManifestsAsync(context, context.Operation.RemoteBranch).ConfigureAwait(false) is not { } remoteBuild)
        {
            context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedDecodeManifestFailed));
            return;
        }

        localBuild = ExtractGameAssetBundles(localBuild);
        remoteBuild = ExtractGameAssetBundles(remoteBuild);

        ImmutableArray<SophonAssetOperation> diffAssets = [.. GetDiffOperations(localBuild, remoteBuild).OrderBy(a => a.Kind)];

        int downloadTotalChunks = GetDownloadTotalBlocks(diffAssets);
        int installTotalChunks = GetInstallTotalBlocks(diffAssets);
        long totalBytes = GetTotalBytes(diffAssets);

        if (!context.EnsureAvailableFreeSpace(totalBytes))
        {
            return;
        }

        InitializeDuplicatedChunkNames(context, diffAssets.SelectMany(a => a.DiffChunks.Select(c => c.AssetChunk)));

        context.Progress.Report(new GamePackageOperationReport.Reset("Copying", 0, localBuild.TotalChunks, localBuild.TotalBytes));
        List<string> usefulChunks = diffAssets
            .Where(ao => ao.Kind is SophonAssetOperationKind.Modify)
            .Select(ao => Path.GetFileName(ao.OldAsset.AssetName))
            .ToList();
        string oldBlksDirectory = Path.Combine(context.Operation.GameFileSystem.GetDataDirectory(), @"StreamingAssets\AssetBundles\blocks");
        foreach (string file in Directory.GetFiles(oldBlksDirectory, "*.blk", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(file);
            if (!usefulChunks.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string newFilePath = Path.Combine(context.Operation.ExtractOrGameDirectory, fileName);
            File.Copy(file, newFilePath, true);
            AssetProperty asset = localBuild.Manifests.Single().ManifestProto.Assets.Single(a => a.AssetName.Contains(fileName, StringComparison.OrdinalIgnoreCase));
            context.Progress.Report(new GamePackageOperationReport.Install(asset.AssetSize, asset.AssetChunks.Count));
        }

        context.Progress.Report(new GamePackageOperationReport.Reset("Extracting", downloadTotalChunks, installTotalChunks, totalBytes));
        await context.Operation.Asset.UpdateDiffAssetsAsync(context, diffAssets).ConfigureAwait(false);

        context.Progress.Report(new GamePackageOperationReport.Finish(context.Operation.Kind));

        SophonDecodedBuild ExtractGameAssetBundles(SophonDecodedBuild decodedBuild)
        {
            SophonDecodedManifest manifest = decodedBuild.Manifests.First();
            SophonManifestProto proto = new();
            proto.Assets.AddRange(manifest.ManifestProto.Assets.Where(asset => AssetBundlesBlockRegex.IsMatch(asset.AssetName)));
            return new(decodedBuild.TotalBytes, [new(manifest.UrlPrefix, proto)]);
        }
    }

    private async ValueTask ExtractExeAsync(GamePackageServiceContext context)
    {
        if (await DecodeManifestsAsync(context, context.Operation.RemoteBranch).ConfigureAwait(false) is not { } remoteBuild)
        {
            context.Progress.Report(new GamePackageOperationReport.Reset(SH.ServiceGamePackageAdvancedDecodeManifestFailed));
            return;
        }

        remoteBuild = ExtractGameExecutable(remoteBuild);

        long totalBytes = remoteBuild.TotalBytes;
        int totalChunks = remoteBuild.TotalChunks;

        if (!context.EnsureAvailableFreeSpace(totalBytes))
        {
            return;
        }

        InitializeDuplicatedChunkNames(context, remoteBuild.Manifests.Single().ManifestProto.Assets.SelectMany(a => a.AssetChunks));

        context.Progress.Report(new GamePackageOperationReport.Reset("Extracting", totalChunks, totalBytes));
        await context.Operation.Asset.InstallAssetsAsync(context, remoteBuild).ConfigureAwait(false);

        context.Progress.Report(new GamePackageOperationReport.Finish(context.Operation.Kind));

        SophonDecodedBuild ExtractGameExecutable(SophonDecodedBuild decodedBuild)
        {
            SophonDecodedManifest manifest = decodedBuild.Manifests.First();
            SophonManifestProto proto = new();
            proto.Assets.Add(manifest.ManifestProto.Assets.Single(a => GameExecutableFileRegex.IsMatch(a.AssetName)));
            return new(proto.Assets.Sum(a => a.AssetSize), [new(manifest.UrlPrefix, proto)]);
        }
    }

    #endregion
}