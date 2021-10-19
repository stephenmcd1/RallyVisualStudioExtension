using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Controls;
using Rally.RestApi;
using Rally.RestApi.Json;
using Rally.RestApi.Response;
using RallyExtension.Extension.ViewModels;

namespace RallyExtension.Extension.Utilities
{
    public class RallyHelper
    {
        private readonly Action<string> _onError;
        private readonly Action<RallyTaskViewModel> _onTaskUpdated;
        private string _lastUserName;
        private string _lastPassword;

        public RallyHelper(Action<string> onError, Action<RallyTaskViewModel> onTaskUpdated)
        {
            _onError = onError;
            _onTaskUpdated = onTaskUpdated;
        }

        public class User
        {
            public User(string displayName)
            {
                DisplayName = displayName;
            }

            public string DisplayName { get; }
        }

        public class QueryResult
        {
            public List<RallyTaskViewModel> Results { get; set; }
            public int TotalResultCount { get; set; }
        }
        private RallyRestApi _apiClient;

        public bool IsLoggedOn => _apiClient?.AuthenticationState == RallyRestApi.AuthenticationResult.Authenticated;

        public async Task<QueryResult> GetTasks(Query query, int pageSize, string order = "FormattedID", int start = 1)
        {
            var request = new Request("task")
            {
                Query = query,
                Fetch = RallyTaskViewModel.FetchFields,
                Limit = pageSize,
                PageSize = pageSize,
                Order =order, Start = start
            };
            var resp = await Task.Run(() => _apiClient.Query(request));
            if (resp.Errors.Any())
            {
                throw new ApplicationException("Error loading tasks: " + string.Join(", ", resp.Errors));
            }

            return new QueryResult
            {
                Results = resp.Results.Select(o => RallyTaskViewModel.FromApi((object) o, SaveTask)).ToList(),
                TotalResultCount = resp.TotalResultCount
            };
        }

        public bool TryLogOn(string userName, string password)
        {
            _apiClient = new RallyRestApi();
            _apiClient.Authenticate(userName, password, allowSSO: false);
            _lastPassword = password;
            _lastUserName = userName;
            return IsLoggedOn;
        }

        public User GetCurrentUser()
        {
            if (!IsLoggedOn)
            {
                return null;
            }

            var res = _apiClient.GetCurrentUser();
            return new User(res.DisplayName);
        }

        private void SaveTask(RallyTaskViewModel item)
        {
            try
            {
                var errors = false;
                Func<string, decimal?> tryParse = str =>
                {
                    if (string.IsNullOrWhiteSpace(str))
                        return null;
                    decimal result;
                    if (decimal.TryParse(str, out result))
                    {
                        return result;
                    }

                    _onError("Could not parse '" + str + "' as a decimal value");
                    errors = true;
                    return null;
                };
                var actuals = tryParse(item.Actuals);
                var todo = tryParse(item.ToDo);
                var estimate = tryParse(item.Estimate);

                if (errors)
                {
                    return;
                }

                var values = new Dictionary<string, object>
                {
                    {"Blocked", item.Blocked},
                    {"BlockedReason", item.BlockedReason},
                    {"State", item.State},
                    {"Actuals", actuals},
                    {"ToDo", todo},
                    {"Estimate", estimate}
                };

                //Make the first attempt at the update
                Func<OperationResult> tryUpdate = () => _apiClient.Update(item.Ref, new DynamicJsonObject(values), new NameValueCollection {{"fetch", string.Join(",", RallyTaskViewModel.FetchFields)}});
                var res = tryUpdate();

                //If it failed, it may be because our session expired.  We could try to look for a message like 'Not authorized to perform action: Invalid key' but that could be fragile
                //   and errors should be rare (?) so we'll just take the safe approach and assume it may have been an expired session.
                if (!res.Success)
                {
                    //Log back on with the last credentials and try the update one more time
                    TryLogOn(_lastUserName, _lastPassword);
                    res = tryUpdate();
                }
                if (res.Success)
                {
                    _onTaskUpdated(RallyTaskViewModel.FromApi(res.Object, SaveTask));
                }
                else
                {
                    _onError("Error saving Task: " + string.Join(",", res.Errors));
                }
            }
            catch (Exception e)
            {
                _onError("Fatal Error saving Task:" + e.Message);
            }
        }

        public bool AddDiscussionToTask(RallyTaskViewModel task, string discussionBody)
        {
            var discussion = new Dictionary<string, object> {{"Text", discussionBody}};

            //Make the first attempt at the update
            Func<OperationResult> tryUpdate = () => _apiClient.AddToCollection(task.Ref, "Discussion", new[] {new DynamicJsonObject(discussion)}.ToList(), null);
            var res = tryUpdate();

            //If it failed, it may be because our session expired.  We could try to look for a message like 'Not authorized to perform action: Invalid key' but that could be fragile
            //   and errors should be rare (?) so we'll just take the safe approach and assume it may have been an expired session.
            if (!res.Success)
            {
                //Log back on with the last credentials and try the update one more time
                TryLogOn(_lastUserName, _lastPassword);
                res = tryUpdate();
            }

            if (!res.Success)
            {
                _onError("Error creating Rally Discussion: " + string.Join(",", res.Errors));
                return false;
            }

            return true;
        }
    }
}