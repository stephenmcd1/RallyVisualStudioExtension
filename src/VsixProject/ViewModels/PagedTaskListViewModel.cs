using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.MVVM;
using Rally.RestApi;
using RallyExtension.Extension.Annotations;
using RallyExtension.Extension.Utilities;

namespace RallyExtension.Extension.ViewModels
{
    /// <summary>
    /// Helper class to handle paging across a collection of tasks
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class PagedTaskListViewModel : INotifyPropertyChanged
    {
        private readonly int _pageSize;
        private readonly Query _query;
        private readonly RallyHelper _rallyApi;
        private readonly Action<string> _onError;
        private readonly Dictionary<int, List<RallyTaskViewModel>> _cachedPages = new Dictionary<int, List<RallyTaskViewModel>>();
        private int _totalItems;

        public ObservableCollection<RallyTaskViewModel> Items { get; } = new ObservableCollection<RallyTaskViewModel>();

        private int _currentPage = 1;
        private int? _totalPages;
        private string _pageInfo = "Loading...";
        private Query _adHocQuery;

        public int CurrentPage
        {
            get { return _currentPage; }
            set
            {
                if (value == _currentPage) return;
                _currentPage = value;
                OnPropertyChanged();
                HandlePageChange();
            }
        }

        public int? TotalPages
        {
            get { return _totalPages; }
            set
            {
                if (value == _totalPages) return;
                _totalPages = value;
                OnPropertyChanged();
                HandlePageChange();
            }
        }

        public string PageInfo
        {
            get { return _pageInfo; }
            set
            {
                if (value == _pageInfo) return;
                _pageInfo = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand MoveToFirstPageCommand { get; }
        public RelayCommand MoveToPreviousPageCommand { get; }
        public RelayCommand MoveToNextPageCommand { get; }

        public PagedTaskListViewModel(int pageSize, Query query, RallyHelper rallyApi, Action<string> onError)
        {
            _pageSize = pageSize;
            _query = query;
            _rallyApi = rallyApi;
            _onError = onError;

            MoveToFirstPageCommand = new RelayCommand(_ => ChangePage(1), _ => CurrentPage != 1);
            MoveToNextPageCommand = new RelayCommand(_ => ChangePage(CurrentPage + 1), _ => TotalPages != null && CurrentPage < TotalPages);
            MoveToPreviousPageCommand = new RelayCommand(_ => ChangePage(CurrentPage - 1), _ => CurrentPage > 1);
        }

        private void HandlePageChange()
        {
            MoveToFirstPageCommand.RaiseCanExecuteChanged();
            MoveToPreviousPageCommand.RaiseCanExecuteChanged();
            MoveToNextPageCommand.RaiseCanExecuteChanged();

            var firstItem = (CurrentPage - 1) * _pageSize + 1;
            var info = "Items " + firstItem + "-" + Math.Min(_totalItems, firstItem + _pageSize - 1);
            if (firstItem + _pageSize < _totalItems)
            {
                info += " of " + _totalItems;
            }

            PageInfo = info;
        }

        private async void ChangePage(int page)
        {
            try
            {
                await LoadPage(page);
            }
            catch (Exception e)
            {
                _onError("Error changing page: " + e.Message);
            }
        }

        public async Task<List<RallyTaskViewModel>> Refresh(Query adHocQuery)
        {
            _adHocQuery = adHocQuery;
            _cachedPages.Clear();
            return await LoadPage(1);
        }

        private async Task<List<RallyTaskViewModel>> LoadPage(int pageNumber)
        {
            List<RallyTaskViewModel> items;
            if (!_cachedPages.TryGetValue(pageNumber, out items))
            {
                var finalQuery = _adHocQuery == null ? _query : _query.And(_adHocQuery);
                var result = await _rallyApi.GetTasks(finalQuery, _pageSize, "LastUpdateDate desc", (pageNumber - 1) * _pageSize + 1);

                _totalItems = result.TotalResultCount;
                TotalPages = _totalItems / _pageSize;
                items = result.Results;
                _cachedPages[pageNumber] = items;
            }

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            CurrentPage = pageNumber;

            return items;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}