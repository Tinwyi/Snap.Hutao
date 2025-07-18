// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Model.Metadata.Item;
using Snap.Hutao.Model.Metadata.Monster;
using Snap.Hutao.Service.Metadata;
using Snap.Hutao.Service.Metadata.ContextAbstraction;
using Snap.Hutao.UI.Xaml.Data;
using System.Collections.Immutable;

namespace Snap.Hutao.ViewModel.Wiki;

[Injection(InjectAs.Scoped)]
[ConstructorGenerated]
internal sealed partial class WikiMonsterViewModel : Abstraction.ViewModel
{
    private readonly IMetadataService metadataService;
    private readonly ITaskContext taskContext;

    private WikiMonsterMetadataContext? metadataContext;

    public IAdvancedCollectionView<Monster>? Monsters
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CurrentChanged -= OnCurrentMonsterChanged;
            }

            SetProperty(ref field, value);

            if (value is not null)
            {
                value.CurrentChanged += OnCurrentMonsterChanged;
            }
        }
    }

    public BaseValueInfo? BaseValueInfo { get; set => SetProperty(ref field, value); }

    protected override async ValueTask<bool> LoadOverrideAsync(CancellationToken token)
    {
        if (await metadataService.InitializeAsync().ConfigureAwait(false))
        {
            try
            {
                metadataContext = await metadataService.GetContextAsync<WikiMonsterMetadataContext>(token).ConfigureAwait(false);

                foreach (Monster monster in metadataContext.Monsters)
                {
                    monster.DropsView ??= monster.Drops?.SelectList(i => metadataContext.IdDisplayItemAndMaterialMap.GetValueOrDefault(i, Material.Default));
                }

                List<Monster> ordered = [.. metadataContext.Monsters.OrderBy(m => m.DescribeId.Value)];

                using (await EnterCriticalSectionAsync().ConfigureAwait(false))
                {
                    IAdvancedCollectionView<Monster> monstersView = ordered.AsAdvancedCollectionView();

                    await taskContext.SwitchToMainThreadAsync();
                    Monsters = monstersView;
                    Monsters.MoveCurrentToFirst();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
            }
        }

        return false;
    }

    private void OnCurrentMonsterChanged(object? sender, object? e)
    {
        UpdateBaseValueInfo(Monsters?.CurrentItem);
    }

    private void UpdateBaseValueInfo(Monster? monster)
    {
        if (metadataContext is null || monster is not { GrowCurves: not null, BaseValue: not null })
        {
            BaseValueInfo = null;
            return;
        }

        BaseValueInfoMetadataContext context = new()
        {
            GrowCurveMap = metadataContext.LevelDictionaryMonsterGrowCurveMap,
            PromoteMap = default,
        };

        BaseValueInfo = new(Monster.MaxLevel, monster.GrowCurves.ToPropertyCurveValues(monster.BaseValue), context);
    }
}