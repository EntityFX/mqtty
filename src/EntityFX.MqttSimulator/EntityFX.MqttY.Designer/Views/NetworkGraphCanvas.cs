using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace EntityFX.MqttY.Designer.Views;

public class NetworkGraphCanvas : Control
{
    // Graph data
    private List<GraphItem> _items = new();
    private List<GraphLink> _links = new();

    // Interaction state
    private GraphItem? _draggedItem;
    private Point _dragStart;
    private Point _itemOriginalPosition;
    private GraphItem? _selectedItem;
    private Point _panStart;
    private Point _offset;
    private double _zoom = 1.0;

    // Multi-selection state
    private HashSet<GraphItem> _selectedItems = new();
    private bool _isRubberBanding;
    private Point _rubberBandStart;
    private Point _rubberBandEnd;
    private bool _isDraggingMultiple;
    private Dictionary<GraphItem, Point> _multiDragOriginalPositions = new();

    // Link creation state
    private bool _isCreatingLink;
    private GraphItem? _linkSource;
    private Point _linkEndPoint;

    // Colors
    private static readonly Color NetworkColor = Color.FromRgb(70, 130, 180);
    private static readonly Color ServerColor = Color.FromRgb(70, 130, 220);
    private static readonly Color ClientColor = Color.FromRgb(80, 200, 80);
    private static readonly Color ApplicationColor = Color.FromRgb(240, 160, 60);
    private static readonly Color SelectedColor = Color.FromRgb(255, 215, 0);
    private static readonly Color MultiSelectedColor = Color.FromRgb(100, 180, 255);
    private static readonly Color LinkColor = Color.FromRgb(180, 180, 180);
    private static readonly Color BackgroundColor = Color.FromRgb(30, 30, 30);
    private static readonly Color GridColor = Color.FromRgb(50, 50, 50);
    private static readonly Color TextColor = Colors.White;
    private static readonly Color RubberBandFill = Color.FromArgb(40, 100, 180, 255);
    private static readonly Color RubberBandBorder = Color.FromArgb(180, 100, 180, 255);

    private static readonly IBrush NetworkBrush = new ImmutableSolidColorBrush(NetworkColor);
    private static readonly IBrush ServerBrush = new ImmutableSolidColorBrush(ServerColor);
    private static readonly IBrush ClientBrush = new ImmutableSolidColorBrush(ClientColor);
    private static readonly IBrush ApplicationBrush = new ImmutableSolidColorBrush(ApplicationColor);
    private static readonly IBrush SelectedBrush = new ImmutableSolidColorBrush(SelectedColor);
    private static readonly IBrush MultiSelectedBrush = new ImmutableSolidColorBrush(MultiSelectedColor);
    private static readonly IBrush LinkBrush = new ImmutableSolidColorBrush(LinkColor);
    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(BackgroundColor);
    private static readonly IBrush GridBrush = new ImmutableSolidColorBrush(GridColor);
    private static readonly IBrush TextBrush = new ImmutableSolidColorBrush(TextColor);
    private static readonly IBrush RubberBandFillBrush = new ImmutableSolidColorBrush(RubberBandFill);
    private static readonly IBrush RubberBandBorderBrush = new ImmutableSolidColorBrush(RubberBandBorder);

    private static readonly IPen LinkPen = new Pen(LinkBrush, 2);
    private static readonly IPen SelectedLinkPen = new Pen(new ImmutableSolidColorBrush(Colors.Yellow), 2);
    private static readonly IPen GridPen = new Pen(GridBrush, 0.5);
    private static readonly IPen RubberBandPen = new Pen(RubberBandBorderBrush, 1);

    // Events
    public event EventHandler<GraphItem>? ItemSelected;
    public event EventHandler<GraphItem>? ItemDoubleClicked;
    public event EventHandler<(GraphItem Source, GraphItem Target)>? LinkCreated;
    public event EventHandler<GraphItem>? ItemDeleteRequested;
    public event EventHandler<(GraphItemType Type, Point Position)>? ItemAddRequested;
    public event EventHandler<IReadOnlySet<GraphItem>>? SelectionChanged;

    public NetworkGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    public void SetGraphData(List<GraphItem> items, List<GraphLink> links)
    {
        _items = items;
        _links = links;
        InvalidateVisual();
    }

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 5.0);
        InvalidateVisual();
    }

    public void SetOffset(Point offset)
    {
        _offset = offset;
        InvalidateVisual();
    }

    public GraphItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Returns the set of all selected items (multi-selection).
    /// </summary>
    public IReadOnlySet<GraphItem> SelectedItems => _selectedItems;

    /// <summary>
    /// Zooms and pans to fit all graph items within the viewport.
    /// </summary>
    public void ZoomToFit()
    {
        if (_items.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var item in _items)
        {
            if (item.Position.X < minX) minX = item.Position.X;
            if (item.Position.Y < minY) minY = item.Position.Y;
            if (item.Position.X + item.Size.Width > maxX) maxX = item.Position.X + item.Size.Width;
            if (item.Position.Y + item.Size.Height > maxY) maxY = item.Position.Y + item.Size.Height;
        }

        var contentWidth = maxX - minX;
        var contentHeight = maxY - minY;

        if (contentWidth <= 0 || contentHeight <= 0) return;

        var padding = 40;
        var viewWidth = Bounds.Width - padding * 2;
        var viewHeight = Bounds.Height - padding * 2;

        if (viewWidth <= 0 || viewHeight <= 0) return;

        var zoomX = viewWidth / contentWidth;
        var zoomY = viewHeight / contentHeight;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.1, 5.0);

        // Center the content
        var contentCenterX = (minX + maxX) / 2;
        var contentCenterY = (minY + maxY) / 2;
        _offset = new Point(
            Bounds.Width / 2 - contentCenterX * _zoom,
            Bounds.Height / 2 - contentCenterY * _zoom);

        InvalidateVisual();
    }

    /// <summary>
    /// Clears multi-selection and resets to single-item mode.
    /// </summary>
    public void ClearMultiSelection()
    {
        _selectedItems.Clear();
        _isRubberBanding = false;
        _isDraggingMultiple = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Draw background
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var baseTransform = context.PushTransform(Matrix.CreateTranslation(_offset.X, _offset.Y));
        var zoomTransform = context.PushTransform(Matrix.CreateScale(_zoom, _zoom));

        DrawGrid(context);
        DrawLinks(context);
        DrawLinkCreation(context);
        DrawItems(context);
        DrawRubberBand(context);

        zoomTransform.Dispose();
        baseTransform.Dispose();
    }

    private void DrawGrid(DrawingContext context)
    {
        var gridSize = 40;
        var viewWidth = Bounds.Width / _zoom;
        var viewHeight = Bounds.Height / _zoom;
        var startX = -_offset.X / _zoom;
        var startY = -_offset.Y / _zoom;

        startX = Math.Floor(startX / gridSize) * gridSize;
        startY = Math.Floor(startY / gridSize) * gridSize;

        for (double x = startX; x < startX + viewWidth + gridSize; x += gridSize)
        {
            context.DrawLine(GridPen, new Point(x, startY), new Point(x, startY + viewHeight + gridSize));
        }
        for (double y = startY; y < startY + viewHeight + gridSize; y += gridSize)
        {
            context.DrawLine(GridPen, new Point(startX, y), new Point(startX + viewWidth + gridSize, y));
        }
    }

    private static Point GetItemCenter(GraphItem item)
    {
        return new Point(
            item.Position.X + item.Size.Width / 2,
            item.Position.Y + item.Size.Height / 2);
    }

    private void DrawLinks(DrawingContext context)
    {
        foreach (var link in _links)
        {
            var from = GetItemCenter(link.From);
            var to = GetItemCenter(link.To);
            var midX = (from.X + to.X) / 2;
            var midY = (from.Y + to.Y) / 2;

            context.DrawLine(LinkPen, from, to);

            // Draw arrow
            DrawArrow(context, from, to);

            // Draw weight label
            if (!string.IsNullOrEmpty(link.Label))
            {
                var ft = new FormattedText(link.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, TextBrush);
                ft.TextAlignment = TextAlignment.Center;
                context.DrawText(ft, new Point(midX - ft.Width / 2, midY - ft.Height / 2 - 16));
            }
        }
    }

    private void DrawLinkCreation(DrawingContext context)
    {
        if (_isCreatingLink && _linkSource != null)
        {
            var sourceCenter = GetItemCenter(_linkSource);
            context.DrawLine(SelectedLinkPen, sourceCenter, _linkEndPoint);
        }
    }

    private void DrawItems(DrawingContext context)
    {
        foreach (var item in _items)
        {
            var isSelected = item == _selectedItem;
            var isMultiSelected = _selectedItems.Contains(item);
            var brush = GetBrushForItem(item, isSelected, isMultiSelected);
            var rect = new Rect(item.Position, item.Size);

            if (item.ItemType == GraphItemType.Network)
            {
                // Network: rounded rectangle
                context.DrawRectangle(brush, null, rect, 8, 8);
            }
            else if (item.ItemType == GraphItemType.Client)
            {
                // Client: ellipse
                context.DrawEllipse(brush, null, rect.Center, rect.Width / 2, rect.Height / 2);
            }
            else
            {
                // Server, Application: rectangle
                context.DrawRectangle(brush, null, rect, 4, 4);
            }

            // Draw border for selected
            if (isSelected || isMultiSelected)
            {
                var borderColor = isSelected ? SelectedColor : MultiSelectedColor;
                var borderPen = new Pen(new ImmutableSolidColorBrush(borderColor), 2);
                if (item.ItemType == GraphItemType.Client)
                    context.DrawEllipse(null, borderPen, rect.Center, rect.Width / 2 + 2, rect.Height / 2 + 2);
                else
                    context.DrawRectangle(null, borderPen, rect.Inflate(2), 8, 8);
            }

            // Draw label
            var label = $"{item.Name}\n({item.SubLabel})";
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, TextBrush);
            ft.TextAlignment = TextAlignment.Center;
            var textX = item.Position.X + (item.Size.Width - ft.Width) / 2;
            var textY = item.Position.Y + (item.Size.Height - ft.Height) / 2;
            context.DrawText(ft, new Point(textX, textY));
        }
    }

    private void DrawRubberBand(DrawingContext context)
    {
        if (!_isRubberBanding) return;

        var canvasStart = ScreenToCanvas(_rubberBandStart);
        var canvasEnd = ScreenToCanvas(_rubberBandEnd);

        var x = Math.Min(canvasStart.X, canvasEnd.X);
        var y = Math.Min(canvasStart.Y, canvasEnd.Y);
        var w = Math.Abs(canvasEnd.X - canvasStart.X);
        var h = Math.Abs(canvasEnd.Y - canvasStart.Y);

        if (w < 1 || h < 1) return;

        var rect = new Rect(x, y, w, h);
        context.FillRectangle(RubberBandFillBrush, rect);
        context.DrawRectangle(RubberBandPen, rect);
    }

    private void DrawArrow(DrawingContext context, Point from, Point to)
    {
        var arrowSize = 8.0;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < arrowSize) return;

        var nx = dx / length;
        var ny = dy / length;

        var arrowPoint = new Point(to.X - nx * 15, to.Y - ny * 15);
        var p1 = new Point(
            arrowPoint.X - nx * arrowSize + (-ny) * arrowSize * 0.5,
            arrowPoint.Y - ny * arrowSize + nx * arrowSize * 0.5);
        var p2 = new Point(
            arrowPoint.X - nx * arrowSize - (-ny) * arrowSize * 0.5,
            arrowPoint.Y - ny * arrowSize - nx * arrowSize * 0.5);

        var arrowGeometry = new StreamGeometry();
        using (var ctx = arrowGeometry.Open())
        {
            ctx.BeginFigure(arrowPoint, true);
            ctx.LineTo(p1);
            ctx.LineTo(p2);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(LinkBrush, null, arrowGeometry);
    }

    private static IBrush GetBrushForItem(GraphItem item, bool isSelected, bool isMultiSelected)
    {
        if (isSelected) return SelectedBrush;
        if (isMultiSelected) return MultiSelectedBrush;
        return item.ItemType switch
        {
            GraphItemType.Network => NetworkBrush,
            GraphItemType.Server => ServerBrush,
            GraphItemType.Client => ClientBrush,
            GraphItemType.Application => ApplicationBrush,
            _ => NetworkBrush
        };
    }

    private GraphItem? HitTest(Point point)
    {
        var transformedPoint = ScreenToCanvas(point);
        // Iterate in reverse to hit top-most items first
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            var rect = new Rect(item.Position, item.Size);
            if (rect.Contains(transformedPoint))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Returns all items whose bounding box intersects the given rectangle (in canvas coordinates).
    /// </summary>
    private List<GraphItem> HitTestRect(Rect rect)
    {
        var result = new List<GraphItem>();
        foreach (var item in _items)
        {
            var itemRect = new Rect(item.Position, item.Size);
            if (rect.Intersects(itemRect))
                result.Add(item);
        }
        return result;
    }

    private Point ScreenToCanvas(Point screenPoint)
    {
        return new Point(
            (screenPoint.X - _offset.X) / _zoom,
            (screenPoint.Y - _offset.Y) / _zoom);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        var hitItem = HitTest(point);
        var isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Context menu
            if (hitItem != null)
            {
                if (!_selectedItems.Contains(hitItem))
                {
                    // If right-click on non-selected item, select it alone
                    ClearMultiSelection();
                    _selectedItem = hitItem;
                    _selectedItems.Add(hitItem);
                    ItemSelected?.Invoke(this, hitItem);
                }
                InvalidateVisual();
            }
            ShowContextMenu(point, hitItem);
            return;
        }

        if (hitItem != null)
        {
            if (isCtrl)
            {
                // Ctrl+Click: toggle item in multi-selection
                if (_selectedItems.Contains(hitItem))
                {
                    _selectedItems.Remove(hitItem);
                    if (_selectedItems.Count == 0)
                    {
                        _selectedItem = null;
                        ItemSelected?.Invoke(this, null!);
                    }
                    else
                    {
                        // Keep the last selected item as the primary
                        _selectedItem = hitItem;
                    }
                }
                else
                {
                    _selectedItems.Add(hitItem);
                    _selectedItem = hitItem;
                    ItemSelected?.Invoke(this, hitItem);
                }

                // Start multi-drag if we have multiple items
                if (_selectedItems.Count > 1)
                {
                    _isDraggingMultiple = true;
                    _dragStart = point;
                    _multiDragOriginalPositions = _selectedItems.ToDictionary(i => i, i => i.Position);
                }
                else
                {
                    _draggedItem = hitItem;
                    _dragStart = point;
                    _itemOriginalPosition = hitItem.Position;
                }

                InvalidateVisual();
                NotifySelectionChanged();
                return;
            }

            // Without Ctrl: single selection
            if (_selectedItems.Count > 1 && _selectedItems.Contains(hitItem))
            {
                // Clicking on a multi-selected item: start multi-drag
                _isDraggingMultiple = true;
                _dragStart = point;
                _multiDragOriginalPositions = _selectedItems.ToDictionary(i => i, i => i.Position);
            }
            else
            {
                // Single selection
                ClearMultiSelection();
                _selectedItem = hitItem;
                _selectedItems.Add(hitItem);
                ItemSelected?.Invoke(this, hitItem);

                _draggedItem = hitItem;
                _dragStart = point;
                _itemOriginalPosition = hitItem.Position;
            }

            InvalidateVisual();
            NotifySelectionChanged();

            // Check for double click
            if (e.ClickCount == 2)
            {
                ItemDoubleClicked?.Invoke(this, hitItem);
            }
        }
        else
        {
            if (isCtrl)
            {
                // Ctrl+Click on empty space: start rubber band selection
                _isRubberBanding = true;
                _rubberBandStart = point;
                _rubberBandEnd = point;
                _draggedItem = null;
                return;
            }

            // Click on empty space: clear selection, start panning
            ClearMultiSelection();
            _selectedItem = null;
            _selectedItems.Clear();
            ItemSelected?.Invoke(this, null!);
            _panStart = point;
            _draggedItem = null;
            InvalidateVisual();
            NotifySelectionChanged();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);

        if (_isRubberBanding)
        {
            _rubberBandEnd = point;
            InvalidateVisual();
            return;
        }

        if (_isDraggingMultiple)
        {
            var delta = point - _dragStart;
            var deltaCanvas = new Point(delta.X / _zoom, delta.Y / _zoom);

            foreach (var kvp in _multiDragOriginalPositions)
            {
                kvp.Key.Position = new Point(
                    kvp.Value.X + deltaCanvas.X,
                    kvp.Value.Y + deltaCanvas.Y);
            }
            InvalidateVisual();
            return;
        }

        if (_draggedItem != null)
        {
            var delta = point - _dragStart;
            _draggedItem.Position = new Point(
                _itemOriginalPosition.X + delta.X / _zoom,
                _itemOriginalPosition.Y + delta.Y / _zoom);
            InvalidateVisual();
        }
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Pan
            var delta = point - _panStart;
            _offset = new Point(_offset.X + delta.X, _offset.Y + delta.Y);
            _panStart = point;
            InvalidateVisual();
        }

        if (_isCreatingLink)
        {
            _linkEndPoint = ScreenToCanvas(point);
            InvalidateVisual();
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isRubberBanding)
        {
            _isRubberBanding = false;

            // Find all items inside the rubber band rectangle
            var canvasStart = ScreenToCanvas(_rubberBandStart);
            var canvasEnd = ScreenToCanvas(_rubberBandEnd);
            var x = Math.Min(canvasStart.X, canvasEnd.X);
            var y = Math.Min(canvasStart.Y, canvasEnd.Y);
            var w = Math.Abs(canvasEnd.X - canvasStart.X);
            var h = Math.Abs(canvasEnd.Y - canvasStart.Y);

            if (w > 5 && h > 5)
            {
                var rect = new Rect(x, y, w, h);
                var hitItems = HitTestRect(rect);

                if (hitItems.Count > 0)
                {
                    _selectedItems.Clear();
                    foreach (var item in hitItems)
                    {
                        _selectedItems.Add(item);
                    }
                    _selectedItem = hitItems.Last();
                    ItemSelected?.Invoke(this, _selectedItem);
                    NotifySelectionChanged();
                }
            }

            InvalidateVisual();
            return;
        }

        if (_isDraggingMultiple)
        {
            _isDraggingMultiple = false;
            _multiDragOriginalPositions.Clear();
            return;
        }

        if (_isCreatingLink && _linkSource != null)
        {
            var point = e.GetPosition(this);
            var hitItem = HitTest(point);
            if (hitItem != null && hitItem != _linkSource && hitItem.ItemType == GraphItemType.Network)
            {
                LinkCreated?.Invoke(this, (_linkSource, hitItem));
            }
            _isCreatingLink = false;
            _linkSource = null;
            InvalidateVisual();
        }

        _draggedItem = null;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom + delta * 0.1, 0.1, 5.0);

        // Zoom towards mouse position
        var mousePos = e.GetPosition(this);
        _offset = new Point(
            mousePos.X - (mousePos.X - _offset.X) * (_zoom / oldZoom),
            mousePos.Y - (mousePos.Y - _offset.Y) * (_zoom / oldZoom));

        InvalidateVisual();
    }

    private void NotifySelectionChanged()
    {
        SelectionChanged?.Invoke(this, _selectedItems);
    }

    private void ShowContextMenu(Point screenPoint, GraphItem? item)
    {
        var menu = new ContextMenu();
        var canvasPos = ScreenToCanvas(screenPoint);

        if (item != null)
        {
            var editItem = new MenuItem { Header = $"Edit {item.Name}" };
            editItem.Click += (_, _) => ItemDoubleClicked?.Invoke(this, item);
            menu.Items.Add(editItem);

            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) =>
            {
                // If multiple items selected, delete all; otherwise delete the clicked item
                if (_selectedItems.Count > 1 && _selectedItems.Contains(item))
                {
                    foreach (var si in _selectedItems.ToList())
                        ItemDeleteRequested?.Invoke(this, si);
                }
                else
                {
                    ItemDeleteRequested?.Invoke(this, item);
                }
            };
            menu.Items.Add(deleteItem);

            menu.Items.Add(new Separator());
        }

        // Add items at cursor position
        var addNetwork = new MenuItem { Header = "Add Network Here" };
        addNetwork.Click += (_, _) => ItemAddRequested?.Invoke(this, (GraphItemType.Network, canvasPos));
        menu.Items.Add(addNetwork);

        var addClient = new MenuItem { Header = "Add Client Here" };
        addClient.Click += (_, _) => ItemAddRequested?.Invoke(this, (GraphItemType.Client, canvasPos));
        menu.Items.Add(addClient);

        var addServer = new MenuItem { Header = "Add Server Here" };
        addServer.Click += (_, _) => ItemAddRequested?.Invoke(this, (GraphItemType.Server, canvasPos));
        menu.Items.Add(addServer);

        var addApp = new MenuItem { Header = "Add Application Here" };
        addApp.Click += (_, _) => ItemAddRequested?.Invoke(this, (GraphItemType.Application, canvasPos));
        menu.Items.Add(addApp);

        if (item?.ItemType == GraphItemType.Network)
        {
            menu.Items.Add(new Separator());
            var createLink = new MenuItem { Header = "Create Link" };
            createLink.Click += (_, _) =>
            {
                _isCreatingLink = true;
                _linkSource = item;
                _linkEndPoint = item.Position;
            };
            menu.Items.Add(createLink);
        }

        menu.Open(this);
    }
}

public enum GraphItemType
{
    Network,
    Server,
    Client,
    Application
}

public class GraphItem
{
    public string Name { get; set; } = string.Empty;
    public string SubLabel { get; set; } = string.Empty;
    public GraphItemType ItemType { get; set; }
    public Point Position { get; set; }
    public Size Size { get; set; } = new(120, 60);
    public object? Tag { get; set; }
}

public class GraphLink
{
    public GraphItem From { get; set; } = null!;
    public GraphItem To { get; set; } = null!;
    public string Label { get; set; } = string.Empty;
    public object? Tag { get; set; }
}