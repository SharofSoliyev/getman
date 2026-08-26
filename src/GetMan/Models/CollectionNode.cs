using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

/// <summary>A node in the sidebar tree: a collection root, a folder, or a saved request.</summary>
public partial class CollectionNode : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "New Request";
    [ObservableProperty] private NodeKind _kind = NodeKind.Request;
    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty] private bool _isExpanded;

    // Transient view state: never worth persisting, and a half-finished rename
    // must not come back after a restart.
    [ObservableProperty][property: JsonIgnore] private bool _isSelected;
    [ObservableProperty][property: JsonIgnore] private bool _isRenaming;
    [ObservableProperty][property: JsonIgnore] private bool _isVisible = true;

    /// <summary>Populated for <see cref="NodeKind.Request"/> nodes.</summary>
    public RequestModel Request { get; set; }

    /// <summary>
    /// Folder/collection level auth, scripts and variables (inherited downward).
    /// Defaults to <see cref="AuthType.Inherit"/> so a folder without its own auth
    /// keeps falling through to its parent, exactly like Postman.
    /// </summary>
    public AuthConfig Auth { get; set; } = new() { Type = AuthType.Inherit };
    [ObservableProperty] private string _preRequestScript = string.Empty;
    [ObservableProperty] private string _testScript = string.Empty;

    public ObservableCollection<KeyValueItem> Variables { get; set; } = new();
    public ObservableCollection<CollectionNode> Children { get; set; } = new();

    [JsonIgnore] public CollectionNode Parent { get; set; }

    [JsonIgnore] public bool IsRequest => Kind == NodeKind.Request;
    [JsonIgnore] public bool IsContainer => Kind != NodeKind.Request;
    [JsonIgnore] public string MethodLabel => Request?.Method ?? string.Empty;

    partial void OnKindChanged(NodeKind value)
    {
        OnPropertyChanged(nameof(IsRequest));
        OnPropertyChanged(nameof(IsContainer));
    }

    public void FixupParents()
    {
        foreach (var c in Children)
        {
            c.Parent = this;
            c.FixupParents();
        }
    }

    public IEnumerable<CollectionNode> Flatten()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var n in c.Flatten())
                yield return n;
    }

    public IEnumerable<CollectionNode> AncestorsAndSelf()
    {
        var n = this;
        while (n != null)
        {
            yield return n;
            n = n.Parent;
        }
    }

    public string PathString()
    {
        var parts = AncestorsAndSelf().Select(n => n.Name).Reverse().ToList();
        return string.Join(" / ", parts);
    }

    public CollectionNode DeepClone()
    {
        var c = new CollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Name,
            Kind = Kind,
            Description = Description,
            Request = Request?.Clone(),
            Auth = Auth?.Clone() ?? new AuthConfig(),
            PreRequestScript = PreRequestScript,
            TestScript = TestScript,
            IsExpanded = IsExpanded
        };
        foreach (var v in Variables) c.Variables.Add(v.Clone());
        foreach (var ch in Children)
        {
            var cc = ch.DeepClone();
            cc.Parent = c;
            c.Children.Add(cc);
        }
        return c;
    }
}
