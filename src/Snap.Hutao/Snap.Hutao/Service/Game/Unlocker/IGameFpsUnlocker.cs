// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Service.Game.Unlocker;

internal interface IGameFpsUnlocker
{
    ValueTask PostUnlockAsync(CancellationToken token = default);

    ValueTask<bool> UnlockAsync(CancellationToken token = default);
}