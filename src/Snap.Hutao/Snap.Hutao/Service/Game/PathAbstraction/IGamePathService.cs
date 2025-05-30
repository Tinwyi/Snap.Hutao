// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Service.Game.PathAbstraction;

internal interface IGamePathService
{
    ValueTask<ValueResult<bool, string>> SilentGetGamePathAsync();

    ValueTask SilentLocateAllGamePathAsync();
}