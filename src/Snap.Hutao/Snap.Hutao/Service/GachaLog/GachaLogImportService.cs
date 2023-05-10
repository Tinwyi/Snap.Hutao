﻿// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.Database;
using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.Entity.Database;
using Snap.Hutao.Model.InterChange.GachaLog;

namespace Snap.Hutao.Service.GachaLog;

/// <summary>
/// 祈愿记录导入服务
/// </summary>
[ConstructorGenerated]
[Injection(InjectAs.Scoped, typeof(IGachaLogImportService))]
internal sealed partial class GachaLogImportService : IGachaLogImportService
{
    private readonly ILogger<GachaLogImportService> logger;
    private readonly IServiceProvider serviceProvider;
    private readonly ITaskContext taskContext;

    /// <inheritdoc/>
    public async Task<GachaArchive> ImportFromUIGFAsync(GachaLogServiceContext context, List<UIGFItem> list, string uid)
    {
        await taskContext.SwitchToBackgroundAsync();
        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            AppDbContext appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            GachaArchiveInitializationContext initContext = new(taskContext, uid, appDbContext.GachaArchives, context.ArchiveCollection);
            GachaArchive.Init(initContext, out GachaArchive? archive);
            Guid archiveId = archive.InnerId;

            long trimId = appDbContext.GachaItems
                .Where(i => i.ArchiveId == archiveId)
                .OrderBy(i => i.Id)
                .FirstOrDefault()?.Id ?? long.MaxValue;

            logger.LogInformation("Last Id to trim with: [{id}]", trimId);

            IEnumerable<GachaItem> toAdd = list
                .OrderByDescending(i => i.Id)
                .Where(i => i.Id < trimId)
                .Select(i => GachaItem.Create(archiveId, i, context.GetItemId(i)));

            await appDbContext.GachaItems.AddRangeAndSaveAsync(toAdd).ConfigureAwait(false);
            return archive;
        }
    }
}