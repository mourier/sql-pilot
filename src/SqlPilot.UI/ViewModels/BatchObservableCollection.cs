using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SqlPilot.UI.ViewModels
{
    /// <summary>
    /// ObservableCollection that supports batch replacement with a single Reset notification
    /// instead of N individual Add notifications.
    /// </summary>
    public class BatchObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IList<T> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
