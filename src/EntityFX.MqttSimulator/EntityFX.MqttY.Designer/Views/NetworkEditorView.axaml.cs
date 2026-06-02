using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using EntityFX.MqttY.Designer.Models;
using EntityFX.MqttY.Designer.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;

namespace EntityFX.MqttY.Designer.Views;

public partial class NetworkEditorView : ReactiveUserControl<NetworkEditorViewModel>
{
    public NetworkEditorView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            if (ViewModel == null) return;

            // Initial graph data binding
            ViewModel.WhenAnyValue(vm => vm.GraphItems)
                .Subscribe(items => GraphCanvas.SetGraphData(items, ViewModel.GraphLinks))
                .DisposeWith(disposables);

            ViewModel.WhenAnyValue(vm => vm.GraphLinks)
                .Subscribe(_ => GraphCanvas.SetGraphData(ViewModel.GraphItems, ViewModel.GraphLinks))
                .DisposeWith(disposables);

            // Canvas events -> ViewModel
            GraphCanvas.ItemSelected += (_, item) =>
            {
                ViewModel.SelectedGraphItem = item;
            };

            GraphCanvas.ItemDoubleClicked += async (_, item) =>
            {
                await ViewModel.EditGraphItemAsync(item);
            };

            GraphCanvas.LinkCreated += (_, link) =>
            {
                // Create link between two networks
                if (link.Source.Tag is NetworkNodeDesignModel sourceNetwork &&
                    link.Target.Tag is NetworkNodeDesignModel targetNetwork)
                {
                    sourceNetwork.Links.Add(new LinkDesignModel
                    {
                        TargetNetwork = targetNetwork.Name,
                        Weight = 1
                    });
                    ViewModel.RebuildGraph();
                }
            };

            // Delete item from context menu
            GraphCanvas.ItemDeleteRequested += (_, item) =>
            {
                ViewModel.DeleteGraphItemCommand.Execute(item);
            };

            // Add item at position from context menu
            GraphCanvas.ItemAddRequested += (_, args) =>
            {
                ViewModel.AddGraphItemAtPositionCommand.Execute(args);
            };

            // Selection sync: ViewModel -> Canvas
            ViewModel.WhenAnyValue(vm => vm.SelectedGraphItem)
                .Subscribe(item => GraphCanvas.SelectedItem = item)
                .DisposeWith(disposables);

            // Zoom sync: ViewModel -> Canvas
            ViewModel.WhenAnyValue(vm => vm.ZoomLevel)
                .Subscribe(zoom => GraphCanvas.SetZoom(zoom))
                .DisposeWith(disposables);

            // Double-click on Network Types DataGrid to open editor
            SubscribeDataGridDoubleClick(NetworkTypesGrid, async () =>
            {
                if (ViewModel.SelectedNetworkType != null)
                    await ViewModel.EditNetworkTypeAsync(ViewModel.SelectedNetworkType);
            });
        });
    }

    private static void SubscribeDataGridDoubleClick(DataGrid grid, Func<Task> handler)
    {
        grid.PointerPressed += async (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                await handler();
            }
        };
    }
}