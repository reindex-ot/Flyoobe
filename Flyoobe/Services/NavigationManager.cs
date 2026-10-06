namespace Flyoobe3.Services;

//Pages with a running operation can briefly keep the shared shell from navigating away.
internal interface INavigationGuard
{
    bool CanNavigateAway { get; }
}

//puts one UserControl into a host panel and remembers the home page
//detail pages are disposed on the way back, while Settings can reuse its three small subpages
internal sealed class NavigationManager
{
    private readonly Panel _host;
    private Control? _home;
    private Control? _current;

    public NavigationManager(Panel host)
    {
        _host = host;
        _host.AutoScroll = true;
        _host.Resize += Host_Resize;
    }

    public void SetHome(Control view)
    {
        _home = view;
        Switch(view, false);
    }

    public void Show(Control view) => Switch(view, true);

    //settings keeps its small subpages alive and simply swaps the visible one
    public void SwitchView(Control view) => Switch(view, false);

    public void Back()
    {
        if (_home != null) Switch(_home, true);
    }

    private void Switch(Control view, bool disposeDetail)
    {
        if (ReferenceEquals(_current, view)) return;
        var old = _current;

        _host.SuspendLayout();
        _host.Controls.Clear();
        //Most pages resize naturally. A page can opt into a fixed canvas through
        //MinimumSize; only those pages receive native host scrollbars.
        if (view.MinimumSize.IsEmpty)
        {
            view.Dock = DockStyle.Fill;
        }
        else
        {
            view.Dock = DockStyle.None;
            view.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            view.Location = Point.Empty;
        }
        _host.Controls.Add(view);
        _current = view;
        _host.AutoScrollPosition = Point.Empty;
        ResizeCurrentView();
        _host.ResumeLayout();

        if (disposeDetail && old != null && !ReferenceEquals(old, _home)) old.Dispose();
    }

    private void Host_Resize(object? sender, EventArgs e) => ResizeCurrentView();

    private void ResizeCurrentView()
    {
        if (_current == null || _current.MinimumSize.IsEmpty) return;
        _current.Size = new Size(
            Math.Max(_host.ClientSize.Width, _current.MinimumSize.Width),
            Math.Max(_host.ClientSize.Height, _current.MinimumSize.Height));
    }
}
