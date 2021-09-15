using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.TeamFoundation.MVVM;
using Rally.RestApi.Json;
using RallyExtension.Extension.Annotations;

namespace RallyExtension.Extension.ViewModels
{
    public class RallyTaskViewModel : INotifyPropertyChanged
    {
        private string _origState;
        private bool _origBlocked;
        private string _origBlockedReason;
        private string _origEstimate;
        private string _origToDo;
        private string _origActuals;


        public ICommand OpenItemCommand { get; }
        public ICommand OpenParentCommand { get; }

        public RelayCommand SetStateCommand { get; }

        public RelayCommand SaveChangesCommand { get; }
        public RelayCommand DiscardChangesCommand { get; }

        private RallyTaskViewModel(object originalApiObject, Action<RallyTaskViewModel> saveTask)
        {
            _originalApiObject = originalApiObject;
            _saveTask = saveTask;
            OpenItemCommand = new RelayCommand(_ => Process.Start(DetailUrl));
            OpenParentCommand = new RelayCommand(_ => Process.Start(ParentDetailUrl));
            SetStateCommand = new RelayCommand(s => State = (string) s, s => State != (string) s);
            SaveChangesCommand = new RelayCommand(SaveChanges, _ => IsDirty);
            DiscardChangesCommand = new RelayCommand(DiscardChanges, _ => IsDirty);
        }


        private void DiscardChanges(object args)
        {
            Blocked = _origBlocked;
            BlockedReason = _origBlockedReason;
            State = _origState;
            Actuals = _origActuals;
            Estimate = _origEstimate;
            ToDo = _origToDo;
        }

        private void CheckDirty()
        {
            IsDirty = Blocked != _origBlocked ||
                      (Blocked && BlockedReason != _origBlockedReason) ||
                      State != _origState ||
                      Actuals != _origActuals ||
                      Estimate != _origEstimate ||
                      ToDo != _origToDo;
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            set
            {
                if (value == _isDirty) return;
                _isDirty = value;
                OnPropertyChanged();
                SaveChangesCommand.RaiseCanExecuteChanged();
                DiscardChangesCommand.RaiseCanExecuteChanged();
            }
        }

        private void SaveChanges(object args)
        {
            _saveTask(this);
        }

        public static readonly List<string> FetchFields = new[]
            {"FormattedID", "Name", "Blocked", "BlockedReason", "State", "Release", "Iteration", "Owner", "WorkProduct", "Project", "Actuals", "Estimate", "ToDo"}.ToList();

        private bool _selected;
        private string _state;
        private bool _blocked;
        private string _blockedReason;
        private bool _isDirty;
        private object _originalApiObject;
        private readonly Action<RallyTaskViewModel> _saveTask;
        private string _estimate;
        private string _toDo;
        private string _actuals;

        public RallyTaskViewModel Clone()
        {
            return FromApi(_originalApiObject, _saveTask);
        }

        public static RallyTaskViewModel FromApi(object rawValue, Action<RallyTaskViewModel> saveTask)
        {
            dynamic obj = rawValue;
            //TODO: Make a way to be able to clone things
            var d = (DynamicJsonObject) obj;

            var parent = obj.WorkProduct;
            var t = new RallyTaskViewModel(rawValue, saveTask)
            {
                FormattedId = obj.FormattedID,
                Name = obj.Name,
                Blocked = obj.Blocked,
                State = obj.State,
                Parent = parent._refObjectName,
                ParentFormattedId = parent.FormattedID,
                ParentRef = parent._ref,
                ParentType = parent._type,
                Project = obj.Project?._refObjectName,
                Ref = obj._ref,
                //These fields are optional so may be blank - and the dynamic accessor of this object don't like that
                BlockedReason = d["BlockedReason"] ?? "",
                Release = d["Release"]?._refObjectName ?? "No Release",
                Iteration = d["Iteration"]?._refObjectName ?? "No Iteration",
                Owner = d["Owner"]?._refObjectName,
                Actuals = d["Actuals"]?.ToString() ?? "",
                ToDo = d["ToDo"]?.ToString() ?? "",
                Estimate = d["Estimate"]?.ToString() ?? ""
            };

            t._origBlocked = t.Blocked;
            t._origState = t.State;
            t._origBlockedReason = t.BlockedReason;
            t._origActuals = t.Actuals;
            t._origEstimate = t.Estimate;
            t._origToDo = t.ToDo;

            t.IsDirty = false;

            return t;
        }

        public string FormattedId { get; set; }
        public string Name { get; set; }

        public string State
        {
            get { return _state; }
            set
            {
                if (value == _state) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShortState));
                SetStateCommand.RaiseCanExecuteChanged();
                CheckDirty();
            }
        }

        public bool Blocked
        {
            get { return _blocked; }
            set
            {
                if (value == _blocked) return;
                _blocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BlockedBrush));
                CheckDirty();
            }
        }

        public string BlockedReason
        {
            get { return _blockedReason; }
            set
            {
                if (value == _blockedReason) return;
                _blockedReason = value;
                OnPropertyChanged();
                CheckDirty();
            }
        }

        public string Actuals
        {
            get { return _actuals; }
            set
            {
                if (value == _actuals) return;
                _actuals = value;
                OnPropertyChanged();
                CheckDirty();
            }
        }

        public string ToDo
        {
            get { return _toDo; }
            set
            {
                if (value == _toDo) return;
                _toDo = value;
                OnPropertyChanged();
                CheckDirty();
            }
        }

        public string Estimate
        {
            get { return _estimate; }
            set
            {
                if (value == _estimate) return;
                _estimate = value;
                OnPropertyChanged();
                CheckDirty();
            }
        }

        public string Iteration { get; set; }
        public string Release { get; set; }
        public string Owner { get; set; }
        public string Parent { get; set; }
        public string ParentFormattedId { get; set; }
        public string ParentRef { get; set; }
        public string ParentType { get; set; }

        public string Project { get; set; }

        public string Ref { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (value == _selected) return;
                _selected = value;
                OnPropertyChanged();
            }
        }

        public Brush BlockedBrush => Blocked ? Brushes.PaleVioletRed : null;

        public string ShortState
        {
            get
            {
                switch (State)
                {
                    case "Defined":
                        return "D";
                    case "In-Progress":
                        return "P";
                    case "Completed":
                        return "C";
                    default:
                        return "?";
                }
            }
        }

        public string DetailUrl => GetDetailUrl(Ref, "Task");
        public string ParentDetailUrl => GetDetailUrl(ParentRef, ParentType);

        private string GetTypeAbbreviation(string fullTypeName)
        {
            switch (fullTypeName.ToLower())
            {
                case "hierarchicalrequirement":
                case "userstory":
                    return "ar";
                case "defect":
                    return "df";
                case "defectsuite":
                    return "ds";
                case "task":
                    return "tk";
                case "testcase":
                    return "tc";
                case "testcaseresult":
                    return "tcr";
                case "testset":
                    return "ts";
                case "release":
                    return "rl";
                case "iteration":
                    return "it";
                case "webtab":
                    return "wt";
                case "portfolioitem":
                    return "pi";
                default:
                    throw new NotSupportedException("Unknown type: " + fullTypeName);
            }
        }

        private string GetDetailUrl(string @ref, string type)
        {
            var objectId = long.Parse(@ref.Substring(@ref.LastIndexOf('/') + 1));
            return $"https://rally1.rallydev.com/slm/detail/{GetTypeAbbreviation(type)}/{objectId}";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}