// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Snap.Hutao.Core.Graphics;
using Snap.Hutao.UI.Windowing.Abstraction;
using Snap.Hutao.ViewModel.Game;
using Windows.Graphics;

namespace Snap.Hutao.UI.Xaml.View.Window;

[Injection(InjectAs.Scoped)]
internal sealed partial class GamePackageOperationWindow : Microsoft.UI.Xaml.Window,
    IDisposable,
    IXamlWindowExtendContentIntoTitleBar,
    IXamlWindowClosedHandler
{
    private readonly IServiceScope scope;
    private readonly TaskCompletionSource closeTcs = new();

    public GamePackageOperationWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        scope = serviceProvider.CreateScope();

        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        SizeInt32 size = new(workArea.Height, (int)(workArea.Height * 0.75));
        AppWindow.Resize(size.Scale(0.5 * this.GetRasterizationScale()));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        // Private Window.Closed handler must attach before InitializeController
        Closed += OnWindowClosing;
        this.InitializeController(serviceProvider);
        RootGrid.InitializeDataContext<GamePackageOperationViewModel>(scope.ServiceProvider);
    }

    public FrameworkElement TitleBarCaptionAccess { get => DraggableGrid; }

    public IEnumerable<FrameworkElement> TitleBarPassthrough { get => []; }

    public GamePackageOperationViewModel DataContext { get => (GamePackageOperationViewModel)RootGrid.DataContext; }

    public Task CloseTask { get => closeTcs.Task; }

    public void Dispose()
    {
        scope.Dispose();
    }

    public void OnWindowClosed()
    {
        closeTcs.TrySetResult();
    }

    private static void OnWindowClosing(object sender, WindowEventArgs args)
    {
        GamePackageOperationWindow window = (GamePackageOperationWindow)sender;
        if (!window.DataContext.IsFinished)
        {
            args.Handled = true;
        }
    }

    [Command("CloseCommand")]
    private void CloseWindow()
    {
        Close();
    }
}