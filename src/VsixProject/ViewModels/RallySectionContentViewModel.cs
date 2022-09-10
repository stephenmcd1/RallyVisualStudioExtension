using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Controls;
using Microsoft.TeamFoundation.Controls.WPF.TeamExplorer;
using Microsoft.TeamFoundation.MVVM;
using Microsoft.TeamFoundation.VersionControl.Client;
using Rally.RestApi;
using RallyExtension.Extension.Annotations;
using RallyExtension.Extension.TeamExplorer;
using RallyExtension.Extension.Utilities;

namespace RallyExtension.Extension.ViewModels
{
    public class RallySectionContentViewModel : INotifyPropertyChanged
    {
        private readonly RallyWorkItemSection _parentSection;
        private RallyHelper _rallyApi;

        public RallySectionContentViewModel(RallyWorkItemSection parentSection)
        {
            _parentSection = parentSection;

            //Setup the commands
            LogOnCommand = new RelayCommand(async _ =>
            {
                try
                {
                    //Defer to the helper method and then see how it went
                    var res = await TryLogOn();
                    switch (res)
                    {
                        case true:
                            //On log on, save the values we used for next time and clear out any errors that may have been there from the logon process
                            _parentSection.Options.SetStringOption("UserName", UserNameEntry);
                            _parentSection.Options.SetEncryptedOption("Password", PasswordEntry);
                            IsErrorActive = false;
                            await Refresh();
                            break;
                        case false:
                            //On failure, show an error
                            ShowError("Could not connect to Rally.  Check credentials.");
                            break;
                        case null:
                            //Otherwise, not enough info to log in - show a different error
                            ShowError("User Name and Password required");
                            break;
                    }
                }
                catch (Exception e)
                {
                    ShowError("Unexpected error logging on: " + e.Message);
                }
            }, _ => true);

            LogOutCommand = new RelayCommand(arg => IsLoggedOn = false, _ => true);

            SearchByIdCommand = new RelayCommand(val =>
            {
                //There are a few ways this command may be called so look around to find the bool parameter
                IsSearchingById = Convert.ToBoolean(val as string ?? ((DropDownLink.DropDownMenuCommandParameters) val).Parameter);
                SearchByIdCommand.RaiseCanExecuteChanged();
            }, val => IsLoggedOn && (val == null || IsSearchingById != Convert.ToBoolean(val)));

            AddByIdCommand = new RelayCommand(async arg =>
            {
                //Make sure the entered some text
                var text = arg as string;
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowError("Please enter the ID");
                    return;
                }

                try
                {
                    //Defer to the helper method and show a message if the item isn't found
                    var res = await AddById(text);
                    if (!res)
                    {
                        ShowError("Could not find Task with ID: " + text);
                    }
                }
                catch (Exception e)
                {
                    ShowError("Fatal error searching by ID: " + e.Message);
                }
            });

            DismissErrorsCommand = new RelayCommand(_ => IsErrorActive = false);

            ClearCurrentItemCommand = new RelayCommand(_ => CurrentItem = null, _ => CurrentItem != null);

            SelectItemCommand = new RelayCommand(parm =>
            {
                var args = parm as ItemDoubleClickEventArgs;
                var model = args?.SelectedItem as RallyTaskViewModel;
                if (model != null)
                {
                    CurrentItem = model.Clone();
                }
            });

            ToggleFilterCommand = new RelayCommand(() => IsFilterActive = !IsFilterActive);

            RefreshCommand = new RelayCommand(() => HandleAsyncErrors(Refresh, "Error when executing refresh command"));

            //Setup some initial variables - especially to trigger the property change logic to get everything in a good state
            Initializing = true;
            IsLoggedOn = false;
            CurrentItem = null;
        }

        private async Task<bool> AddById(string id)
        {
            var tasks = await _rallyApi.GetTasks(new Query("FormattedID", Query.Operator.Equals, id),  1);

            if (tasks.Results.Count == 0)
            {
                return false;
            }

            IsSearchingById = false;
            SearchByIdCommand.RaiseCanExecuteChanged();
            CurrentItem = tasks.Results[0];
            if (CurrentItem.Owner != CurrentUserDisplayName)
            {
                ShowError("Warning: You are not the Owner of the current item");
            }

            return true;
        }

        private async Task<bool?> TryLogOn()
        {
            if (string.IsNullOrWhiteSpace(UserNameEntry) || string.IsNullOrWhiteSpace(PasswordEntry))
            {
                return null;
            }

            var res = await Task.Run(() => _rallyApi.TryLogOn(UserNameEntry, PasswordEntry));
            if (!res)
            {
                return false;
            }

            var user = await Task.Run(() => _rallyApi.GetCurrentUser());
            CurrentUserDisplayName = user.DisplayName;

            var query = new Query("Owner.DisplayName", Query.Operator.Equals, CurrentUserDisplayName);
            CurrentList = new PagedTaskListViewModel(5, query, _rallyApi, ShowError);

            IsLoggedOn = true;

            return true;
        }

        private Query BuildAdHocFilter()
        {
            if (!IsFilterActive)
            {
                return null;
            }

            Query filter = null;
            if (ExcludeCompleteTasks)
            {
                filter = new Query("State", Query.Operator.DoesNotEqual, "Completed");
            }

            if (!string.IsNullOrWhiteSpace(NameFilter))
            {
                var nameFilter = new Query("Name", Query.Operator.Contains, NameFilter);
                filter = filter == null ? nameFilter : filter.And(nameFilter);
            }

            return filter;
        }

        public async Task Refresh()
        {
            _parentSection.IsBusy = true;

            if (!_rallyApi.IsLoggedOn)
            {
                _parentSection.IsBusy = false;
                return;
            }

            var items = await CurrentList.Refresh(BuildAdHocFilter());

            var updatedCurrent = items.SingleOrDefault(item => item.FormattedId == CurrentItem?.FormattedId);
            if (CurrentItem != null && updatedCurrent == null)
            {
                if (!await AddById(CurrentItem.FormattedId))
                {
                    ShowError("Previously selected item (" + CurrentItem.FormattedId + ") could not be found");
                    CurrentItem = null;
                }
            }

            _parentSection.IsBusy = false;
        }

        public async Task Initialize()
        {
            UserNameEntry = _parentSection.Options.GetStringOption("UserName");
            PasswordEntry = _parentSection.Options.GetEncryptedOption("Password");

            _rallyApi = new RallyHelper(ShowError, updatedTask =>
            {
                CurrentItem = updatedTask;
                HandleAsyncErrors(Refresh, "Error refreshing after save");
            });

            var res = await TryLogOn();
            if (res == false)
            {
                ShowError("Could not connect to Rally with saved credentials");
            }

            await Refresh();

            Initializing = false;
        }

        private void ShowError(string message)
        {
            ErrorMessage = message;
            IsErrorActive = true;
        }

        private async void HandleAsyncErrors(Func<Task> t, string prefix)
        {
            try
            {
                await t();
            }
            catch (Exception e)
            {
                ShowError(prefix + ": " + e.Message);
            }
        }


        #region Properties

        private string _passwordEntry;
        private bool _isLoggedOn;
        private string _currentUserDisplayName;
        private bool _initializing;
        private string _userNameEntry;
        private bool _isSearchingById;
        private bool _isFilterActive;
        private bool _excludeCompleteTasks;
        private string _nameFilter;
        private RallyTaskViewModel _currentItem;
        private bool _isErrorActive;
        private string _errorMessage;
        private PagedTaskListViewModel _currentList;

        public RelayCommand LogOnCommand { get; set; }
        public RelayCommand LogOutCommand { get; set; }
        public RelayCommand SearchByIdCommand { get; set; }
        public RelayCommand AddByIdCommand { get; set; }
        public RelayCommand DismissErrorsCommand { get; set; }
        public RelayCommand ClearCurrentItemCommand { get; set; }
        public RelayCommand SelectItemCommand { get; set; }
        public RelayCommand ToggleFilterCommand { get; set; }

        public RelayCommand RefreshCommand { get; set; }

        public PagedTaskListViewModel CurrentList
        {
            get { return _currentList; }
            set
            {
                if (value == _currentList) return;
                _currentList = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoggedOn
        {
            get { return _isLoggedOn; }
            set
            {
                if (value == _isLoggedOn) return;
                _isLoggedOn = value;
                OnPropertyChanged();
                SearchByIdCommand.RaiseCanExecuteChanged();
                if (!value)
                {
                    IsSearchingById = false;
                    CurrentUserDisplayName = "Not Logged On";
                }
            }
        }

        public string CurrentUserDisplayName
        {
            get { return _currentUserDisplayName; }
            set
            {
                if (value == _currentUserDisplayName) return;
                _currentUserDisplayName = value;
                OnPropertyChanged();
            }
        }

        public bool Initializing
        {
            get { return _initializing; }
            set
            {
                if (value == _initializing) return;
                _initializing = value;
                OnPropertyChanged();
            }
        }

        public string UserNameEntry
        {
            get { return _userNameEntry; }
            set
            {
                if (value == _userNameEntry) return;
                _userNameEntry = value;
                OnPropertyChanged();
            }
        }


        public bool IsSearchingById
        {
            get { return _isSearchingById; }
            set
            {
                if (value == _isSearchingById) return;
                _isSearchingById = value;
                OnPropertyChanged();
            }
        }

        public bool IsFilterActive
        {
            get { return _isFilterActive; }
            set
            {
                if (value == _isFilterActive) return;
                _isFilterActive = value;
                OnPropertyChanged();
                HandleAsyncErrors(Refresh, "Error refreshing as part of filter change");
            }
        }

        public bool ExcludeCompleteTasks
        {
            get { return _excludeCompleteTasks; }
            set
            {
                if (value == _excludeCompleteTasks) return;
                _excludeCompleteTasks = value;
                OnPropertyChanged();
                HandleAsyncErrors(Refresh, "Error refreshing as part of filter change");
            }
        }

        public string NameFilter
        {
            get { return _nameFilter; }
            set
            {
                if (value == _nameFilter) return;
                _nameFilter = value;
                OnPropertyChanged();
            }
        }

        public string PasswordEntry
        {
            get { return _passwordEntry; }
            set
            {
                if (value == _passwordEntry) return;
                _passwordEntry = value;
                OnPropertyChanged();
            }
        }


        public RallyTaskViewModel CurrentItem
        {
            get { return _currentItem; }
            set
            {
                if (Equals(value, _currentItem)) return;
                _currentItem = value;
                OnPropertyChanged();
                ClearCurrentItemCommand.RaiseCanExecuteChanged();
                _parentSection.SetAdditionalExpandedTitle(value == null ? "Nothing Selected" : value.FormattedId + ": " + value.Name);
                foreach (var i in CurrentList.Items)
                {
                    i.Selected = i.FormattedId == CurrentItem?.FormattedId;
                }
            }
        }

        public bool IsErrorActive
        {
            get { return _isErrorActive; }
            set
            {
                if (value == _isErrorActive) return;
                _isErrorActive = value;
                OnPropertyChanged();
            }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set
            {
                if (value == _errorMessage) return;
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        #endregion


        public void OnBeforeCheckIn(out bool shouldContinue)
        {
            if (string.IsNullOrWhiteSpace(_parentSection.CheckinComment))
            {
                UIHost.ShowMessageBox("You must enter a check-in comment before you can checkin these changes", null, "Check-in Comment Required", MessageBoxButtons.OK, MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button1);
                shouldContinue = false;
            }
            else if (CurrentItem == null)
            {
                var res = UIHost.ShowMessageBox(
                    "This checkin is not associated with a Rally item.  Nearly all TFS check-ins should be associated with a Rally item.  Are you sure you want to continue?", null,
                    "No Rally Item Selected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                shouldContinue = (res == DialogResult.Yes);
            }
            else
            {
                var separator = _parentSection.CheckinComment != null && _parentSection.CheckinComment.Contains("\r\n")
                    ? "\r\n"
                    : " ";
                _parentSection.CheckinComment = $"{CurrentItem.ParentFormattedId}-{CurrentItem.FormattedId}:{separator}{_parentSection.CheckinComment}";
                shouldContinue = true;
            }
        }

        public string RemoveTaskPrefix(string comment)
        {
            if (CurrentItem == null || comment == null)
            {
                return comment;
            }

            var prefix = $"^{CurrentItem.ParentFormattedId}-{CurrentItem.FormattedId}:( |\r\n)";
            return Regex.Replace(comment, prefix, "");
        }

        public async void OnAfterCheckin(int changesetId, Workspace workspace)
        {
            if (CurrentItem == null)
            {
                return;
            }

            if (AddDiscussion(changesetId, workspace))
            {
                var itemCopy = CurrentItem;
                _parentSection.ShowNotification($"Rally Discussion added to [{CurrentItem.FormattedId}](Click to open Task in Rally)", NotificationType.Information,
                    new RelayCommand(_ => itemCopy.OpenItemCommand.Execute(null)));
            }

            try
            {
                await CurrentList.Refresh(BuildAdHocFilter());
            }
            catch (Exception e)
            {
                ShowError("Error refreshing tasks: " + e.Message);
            }

            CurrentItem = null;
        }

        private bool AddDiscussion(int changesetId, Workspace workspace)
        {
            var collectionGuid = workspace.VersionControlServer.TeamProjectCollection.InstanceId;
            var changeset = workspace.VersionControlServer.GetChangeset(changesetId, true, true);
            var changesetComment = RemoveTaskPrefix(changeset.Comment);

            var builder = new StringBuilder();

            var changesetHyperlink =
                $"<a href=\"http://vsrssinttools01/DevTools/cs.axd?cs={changesetId}&amp;pcguid={collectionGuid:D}\" alt=\"Navigate to TFS changeset\" target=\"_blank\"><b>{changesetId}</b></a>";


            builder.Append($"<b>Changeset Number:</b> {changesetHyperlink}<br>");
            builder.Append($"<b>Change Owner:</b> *{workspace.OwnerName}*<br>");
            builder.Append("<b>Comment:</b> <br>");

            var comment = string.IsNullOrWhiteSpace(changesetComment)
                ? "No comment provided"
                //Encode the comment as it could have html unsafe characters and make sure it's not too long and finally preserve the line breaks as html
                : WebUtility.HtmlEncode(changesetComment.TrimTo(1000, "..."))
                    .Replace(Environment.NewLine, "<br />");

            builder.Append($"<pre style='padding:3px 2px 3px 5px;width:79%;border:solid 1px #CCCCCC'>{comment}</pre>");

            const int maxChanges = 250;

            builder.Append("<br><b>File(s) changed:</b>");

            if (changeset.Changes.Length <= maxChanges)
            {
                builder.Append(
                    "<table cellpadding='1' cellspacing ='1' Width='80%' bgcolor='#F1EFE2' ><tr><td Width = '20%' >Name</td><td Width = '10%'>Change</td><td>Folder</td></tr>");
                foreach (var change in changeset.Changes)
                {
                    var fileName = $"<b>{change.Item.ServerItem.Substring(change.Item.ServerItem.LastIndexOf("/", StringComparison.Ordinal) + 1)}</b>";
                    builder.Append($"<tr bgcolor='white' ><td>{fileName}</td><td>{change.ChangeType}</td><td>{change.Item.ServerItem}</td></tr>");
                }

                builder.Append("</table>");
            }
            else
            {
                var hereHyperlink = changesetHyperlink.Replace($">{changesetId}<", ">here<");
                builder.Append($"<br>More than {maxChanges} files changed. Please click {hereHyperlink} to view changeset information.");
            }

            return _rallyApi.AddDiscussionToTask(CurrentItem, builder.ToString());
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}