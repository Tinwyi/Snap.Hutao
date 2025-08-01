// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core;

namespace Snap.Hutao.Service.Game.Launching.Handler;

internal sealed class LaunchExecutionGameProcessInitializationHandler : ILaunchExecutionDelegateHandler
{
    public ValueTask<bool> BeforeExecutionAsync(LaunchExecutionContext context, BeforeExecutionDelegate next)
    {
        return next();
    }

    public async ValueTask ExecutionAsync(LaunchExecutionContext context, LaunchExecutionDelegate next)
    {
        if (!context.TryGetGameFileSystem(out IGameFileSystemView? gameFileSystem))
        {
            return;
        }

        context.Progress.Report(new(LaunchPhase.ProcessInitializing, SH.ServiceGameLaunchPhaseProcessInitializing));
        using (context.Process = InitializeGameProcess(context, gameFileSystem))
        {
            await next().ConfigureAwait(false);
        }
    }

    private static System.Diagnostics.Process InitializeGameProcess(LaunchExecutionContext context, IGameFileSystemView gameFileSystem)
    {
        LaunchOptions launchOptions = context.Options;

        string commandLine = string.Empty;
        if (launchOptions.AreCommandLineArgumentsEnabled)
        {
            // https://docs.unity.cn/cn/current/Manual/PlayerCommandLineArguments.html
            // https://docs.unity3d.com/2017.4/Documentation/Manual/CommandLineArguments.html
            commandLine = new CommandLineBuilder()
                .AppendIf(launchOptions.IsBorderless, "-popupwindow")
                .AppendIf(launchOptions.IsExclusive, "-window-mode", "exclusive")
                .Append("-screen-fullscreen", launchOptions.IsFullScreen ? "1" : "0")
                .AppendIf(launchOptions.IsScreenWidthEnabled, "-screen-width", launchOptions.ScreenWidth)
                .AppendIf(launchOptions.IsScreenHeightEnabled, "-screen-height", launchOptions.ScreenHeight)
                .AppendIf(launchOptions.IsMonitorEnabled, "-monitor", launchOptions.Monitor.Value)
                .AppendIf(launchOptions.IsPlatformTypeEnabled, "-platform_type", $"{launchOptions.PlatformType:G}")
                .AppendIf(launchOptions.UsingHoyolabAccount && !string.IsNullOrEmpty(context.AuthTicket), "login_auth_ticket", context.AuthTicket, CommandLineArgumentPrefix.Equal)
                .ToString();

            context.TaskContext.InvokeOnMainThread(() =>
            {
                launchOptions.SaveAspectRatio(new(launchOptions.ScreenWidth, launchOptions.ScreenHeight));
            });
        }

        context.Logger.LogInformation("Command Line Arguments: {commandLine}", commandLine);

        return new()
        {
            StartInfo = new()
            {
                Arguments = commandLine,
                FileName = gameFileSystem.GameFilePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = gameFileSystem.GetGameDirectory(),
            },
        };
    }
}