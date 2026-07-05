using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LevyFlight
{
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification;

        public void ReplaceAll(IEnumerable<T> items)
        {
            _suppressNotification = true;
            try
            {
                Items.Clear();
                foreach (var item in items)
                    Items.Add(item);
            }
            finally
            {
                _suppressNotification = false;
            }
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }
    }
}
