// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Service.Game.Launching.Handler;

internal sealed class LaunchExecutionEnsureSchemeHandler : ILaunchExecutionDelegateHandler
{
    public ValueTask<bool> BeforeExecutionAsync(LaunchExecutionContext context, BeforeExecutionDelegate next)
    {
        return next();
    }

    public async ValueTask ExecutionAsync(LaunchExecutionContext context, LaunchExecutionDelegate next)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (context.TargetScheme is null)
        {
            context.Result.Kind = LaunchExecutionResultKind.NoActiveScheme;
            context.Result.ErrorMessage = SH.ViewModelLaunchGameSchemeNotSelected;
            return;
        }

        context.Logger.LogInformation("TargetScheme [{TargetScheme}] is selected", context.TargetScheme.DisplayName);
        await next().ConfigureAwait(false);
    }
}