using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Docking;

/// <summary>
/// The window's panes, Enterprise Architect's way round: the project browser
/// on the left, properties on the right, documents in the middle with the
/// problems panel under them. Every pane can be resized, floated or docked
/// elsewhere; that is Dock's work, not this file's.
/// </summary>
public sealed class StudioDockFactory(
    BrowserViewModel browser,
    PropertiesViewModel properties,
    BottomPanelViewModel bottom,
    WelcomeViewModel welcome) : Factory
{
    public IDocumentDock Documents { get; private set; } = null!;

    public override IRootDock CreateLayout()
    {
        Documents = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            ActiveDockable = welcome,
            VisibleDockables = CreateList<IDockable>(welcome),
        };

        var bottomDock = new ToolDock
        {
            Id = "BottomDock",
            Proportion = 0.26,
            ActiveDockable = bottom,
            VisibleDockables = CreateList<IDockable>(bottom),
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Hidden,
        };

        var centre = new ProportionalDock
        {
            Id = "Centre",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(Documents, new ProportionalDockSplitter(), bottomDock),
        };

        var left = new ToolDock
        {
            Id = "LeftDock",
            Proportion = 0.19,
            ActiveDockable = browser,
            VisibleDockables = CreateList<IDockable>(browser),
            Alignment = Alignment.Left,
        };

        var right = new ToolDock
        {
            Id = "RightDock",
            Proportion = 0.21,
            ActiveDockable = properties,
            VisibleDockables = CreateList<IDockable>(properties),
            Alignment = Alignment.Right,
        };

        var main = new ProportionalDock
        {
            Id = "Main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                left, new ProportionalDockSplitter(), centre, new ProportionalDockSplitter(), right),
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        root.VisibleDockables = CreateList<IDockable>(main);
        return root;
    }

    /// <summary>Shows a document, adding it first if it is not open yet.</summary>
    public void Show(IDockable document)
    {
        if (Documents.VisibleDockables?.Contains(document) != true)
            AddDockable(Documents, document);

        SetActiveDockable(document);
        SetFocusedDockable(Documents, document);
    }
}
