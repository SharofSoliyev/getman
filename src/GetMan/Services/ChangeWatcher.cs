using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>Fires a callback whenever anything inside a request graph changes.</summary>
public static class ChangeWatcher
{
    public static void Watch(RequestModel request, Action onChanged)
    {
        if (request == null) return;
        request.PropertyChanged += (_, _) => onChanged();
        request.Body.PropertyChanged += (_, _) => onChanged();
        request.Auth.PropertyChanged += (_, _) => onChanged();
        request.Settings.PropertyChanged += (_, _) => onChanged();
        WatchCollection(request.QueryParams, onChanged);
        WatchCollection(request.PathVariables, onChanged);
        WatchCollection(request.Headers, onChanged);
        WatchCollection(request.Body.FormData, onChanged);
        WatchCollection(request.Body.UrlEncoded, onChanged);
    }

    public static void WatchCollection<T>(ObservableCollection<T> collection, Action onChanged)
        where T : INotifyPropertyChanged
    {
        if (collection == null) return;

        foreach (var item in collection)
            item.PropertyChanged += (_, _) => onChanged();

        collection.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (INotifyPropertyChanged item in e.NewItems)
                    item.PropertyChanged += (_, _) => onChanged();
            onChanged();
        };
    }
}
