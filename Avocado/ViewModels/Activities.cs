﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avocado.DataModel;
using MetroMVVM;
using MetroMVVM.Commands;
using MetroMVVM.Threading;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace Avocado.ViewModel
{
    public enum TabType
    {
        Activities, 
        Lists, 
        Media, 
        Preferences,
        Calendar
    }

    class Activities : ObservableObject
    {
        public AuthClient AuthClient { get; set; }

        private CoupleModel couple;
        public CoupleModel Couple { get { return couple; } set { couple = value; RaisePropertyChanged("Couple"); RaisePropertyChanged("NewMessagePrompt"); } }

        private List<Activity> activityList;
        public List<Activity> ActivityList { get { return activityList; } set { activityList = value; RaisePropertyChanged("ActivityList"); } }

        private List<ListModel> listModelList;
        public List<ListModel> ListModelList { get { return listModelList; } set { listModelList = value; RaisePropertyChanged("ListModelList"); } }

        private List<MediaModel> mediaList;
        public List<MediaModel> MediaList { get { return mediaList; } set { mediaList = value; RaisePropertyChanged("MediaList"); } }

        public MediaModel selectedMediaListItem;
        public MediaModel SelectedMediaListItem { get { return selectedMediaListItem; } set { selectedMediaListItem = value; RaisePropertyChanged("SelectedMediaListItem"); } }
        
        private ListModel selectedListModel;
        public ListModel SelectedListModel { 
            get 
            { 
                return selectedListModel; 
            } 
            set 
            {
                if (value == null)
                {
                    return;
                }
                // The value passed will not equal an actual list object in the list model list, so find
                // the real one with linq by the Id, and use that one.
                selectedListModel = (from list in ListModelList
                                     where list.Id == value.Id
                                     select list).First();
                SelectedTab = TabType.Lists;
                selectedListModel.Items.CollectionChanged += new NotifyCollectionChangedEventHandler((t, y) => ListItemDrop(t, y)); 
                RaisePropertyChanged("SelectedListModel"); 
            } 
        }

        private object ListItemDrop(object t, NotifyCollectionChangedEventArgs y)
        {
            if (y.Action == NotifyCollectionChangedAction.Add)
            {
                EditListItem(((ObservableCollection<ListItemModel>)t)[y.NewStartingIndex], false, y.NewStartingIndex);
            }
            return t;
        }

        private string newMessage;
        public string NewMessage { get { return newMessage; } set { newMessage = value; RaisePropertyChanged("NewMessage"); } }

        public string NewMessagePrompt
        {
            get
            {
                if (Couple == null)
                {
                    return string.Empty;
                }
                return string.Format("Send {0} a message...", Couple.OtherUser.FirstName);
            }
        }
        public void NewMessageKeyDown(KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Enter)
            {
                if (!string.IsNullOrEmpty(NewMessage))
                {
                    IsLoading = true;
                    var clone = NewMessage;
                    var task = ThreadPool.RunAsync(t =>
                    {
                        AuthClient.SendMessage(clone);
                        
                    });
                    task.Completed = RunOnComplete(Update);

                }
                NewMessage = "";
            }
        }
        public ICommand NewMessageKeyDownCommand
        {
            get
            {
                return new RelayCommand<KeyRoutedEventArgs>(args => { NewMessageKeyDown(args); });
            }
        }

        private Activity selectedActivity;
        public Activity SelectedActivity 
        { 
            get 
            { 
                return selectedActivity; 
            } 
            set 
            { 
                selectedActivity = value;
                if (value == null)
                {
                    return;
                }
                if (selectedActivity.Type == "list")
                {
                    SelectedListModel = AuthClient.GetList(selectedActivity.Data.Id);
                }
                RaisePropertyChanged("SelectedActivity");
            } 
        }

        public ICommand RefreshCommand
        {
            get
            {
                return new RelayCommand(() => Update() );
            }
        }

        public void Logout()
        {

        }

        public ICommand LogoutCommand
        {
            get
            {
                return new RelayCommand(() => { Logout(); });
            }
        }

        private bool isLoading;
        public bool IsLoading { get { return isLoading; } set { isLoading = value; RaisePropertyChanged("IsLoading"); } }

        #region ListItemActions

        public void EditListItem(ListItemModel listItem, bool delete = false, int index = -1)
        {
            if (string.IsNullOrEmpty(listItem.ListId))
            {
                return;
            }
            IsLoading = true;
            var parentList = (from l in ListModelList
                         where l.Id == listItem.ListId
                         select l).First();

            if (index == -1)
            {
                index = GetListItemIndex(parentList, listItem.Complete, listItem.Important);
                parentList.Items.Move(parentList.Items.IndexOf(listItem), index);
                SelectedListModel.Items = parentList.Items;
            }
            if (delete)
            {
                parentList.Items.Remove(listItem);
            }
            var task = ThreadPool.RunAsync(t => AuthClient.EditListItem(listItem, index, delete));
            task.Completed = RunOnComplete(() => { IsLoading = false; });
        }

        public ICommand EditListItemCommand
        {
            get
            {
                return new RelayCommand<ListItemModel>(param => EditListItem(param));
            }
        }

        public void ToggleListItemImportant(ListItemModel listItem)
        {
            listItem.Important = !listItem.Important;
            EditListItem(listItem);
        }

        public ICommand ToggleListItemImportantCommand
        {
            get
            {
                return new RelayCommand<ListItemModel>(param => { ToggleListItemImportant(param); });
            }
        }

        public ICommand DeleteListItemCommand
        {
            get
            {
                return new RelayCommand<ListItemModel>(param => EditListItem(param, true));
            }
        }

        private string newListItemText;
        public string NewListItemText
        {
            get
            {
                return newListItemText;
            }
            set
            {
                newListItemText = value;
                RaisePropertyChanged("NewListItemText");
            }
        }

        public void NewListItemKeyDown(KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Enter)
            {
                if (!string.IsNullOrEmpty(NewListItemText))
                {
                    IsLoading = true;
                    var index = GetListItemIndex(SelectedListModel, false, false);
                    SelectedListModel.Items.Insert(index, new ListItemModel() { Text = NewListItemText });
                    var clone = NewListItemText;
                    var task = ThreadPool.RunAsync(t => AuthClient.CreateListItem(clone, SelectedListModel.Id, index));

                    task.Completed = RunOnComplete(Update);

                }
                NewListItemText = "";
            }
        }

        public ICommand NewListItemKeyDownCommand
        {
            get
            {
                return new RelayCommand<KeyRoutedEventArgs>(param => NewListItemKeyDown(param));
            }
        }

        public int GetListItemIndex(ListModel list, bool complete, bool important)
        {
            if (important && !complete) // important and not complete, just add it to the beginning of the list
            {
                return 0;
            }
            else if(!important && !complete) // not important and not complete, add it after all important items
            {
                return (from l in list.Items
                        where l.Important && !l.Complete
                        select l).Count();
            } 
            else // any completed item can be added moved to the end of all uncompleted items 
            {
                return (from l in list.Items
                        where !l.Complete
                        select l).Count();
            }
        }

        #endregion

        #region SelectedTab
        private TabType selectedTab;
        public TabType SelectedTab
        {
            get
            {
                return selectedTab;
            }
            set
            {
                selectedTab = value;
                RaisePropertyChanged("SelectedTab");
                RaisePropertyChanged("IsActivityTabActive");
                RaisePropertyChanged("IsMediaTabActive");
                RaisePropertyChanged("IsListTabActive");
                RaisePropertyChanged("IsCalendarTabActive");
                RaisePropertyChanged("IsPreferenceTabActive");
            }
        }
        
        public bool IsListTabActive
        {
            get
            {
                return SelectedTab == TabType.Lists;
            }
        }
        
        public bool IsMediaTabActive
        {
            get
            {
                return SelectedTab == TabType.Media;
            }
        }
        
        public bool IsActivityTabActive
        {
            get
            {
                return SelectedTab == TabType.Activities;
            }
        }
        
        public bool IsPreferenceTabActive
        {
            get
            {
                return SelectedTab == TabType.Preferences;
            }
        }

        public bool IsCalendarTabActive
        {
            get
            {
                return SelectedTab == TabType.Calendar;
            }
        }

        public void SetTabActive(TabType tab)
        {
            SelectedTab = tab;
        }

        public ICommand SetActivityTabActiveCommand
        {
            get
            {
                return new RelayCommand(() => { SetTabActive(TabType.Activities); });
            }
        }

        public ICommand SetMediaTabActiveCommand
        {
            get
            {
                return new RelayCommand(() => { SetTabActive(TabType.Media); });
            }
        }

        public ICommand SetListTabActiveCommand
        {
            get
            {
                return new RelayCommand(() => { SetTabActive(TabType.Lists); });
            }
        }

        public ICommand SetPreferenceTabActiveCommand
        {
            get
            {
                return new RelayCommand(() => { SetTabActive(TabType.Preferences); });
            }
        }

        public ICommand SetCalendarTabActiveCommand
        {
            get
            {
                return new RelayCommand(() => { SetTabActive(TabType.Calendar); }); 
            }
        }
        #endregion

        #region Calendar

        private ObservableCollection<CalendarItem> calendarItems;
        public ObservableCollection<CalendarItem> CalendarItems
        {
            get { return calendarItems; }
            set { calendarItems = value; RaisePropertyChanged("CalendarItems"); }
        }

        private DateTime selectedDate;
        public DateTime SelectedDate
        {
            get
            {
                return selectedDate;
            }
            set
            {
                selectedDate = value;
                RaisePropertyChanged("SelectedDate");
            }
        }

        private CalendarItem selectedCalendarItem;
        public CalendarItem SelectedCalendarItem
        {
            get
            {
                return selectedCalendarItem;
            }
            set
            {
                selectedCalendarItem = value;
                RaisePropertyChanged("SelectedCalendarItem");
            }
        }

        public void SelectDate(DateTime date)
        {

        }

        public ICommand SelectDateCommand
        {
            get
            {
                return new RelayCommand<DateTime>(d => SelectDate(d));
            }
        }

        #endregion

        #region Media

        public void AddPhoto()
        {
            // We need to run this async, but on the UI thread (... yeah, me either)
            DispatcherHelper.CheckBeginInvokeOnUI(() => { PickPhoto(); });
        }

        public async void PickPhoto()
        {
            FileOpenPicker open = new FileOpenPicker();
            open.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            open.ViewMode = PickerViewMode.Thumbnail;

            // Filter to include a sample subset of file types
            open.FileTypeFilter.Clear();
            open.FileTypeFilter.Add(".bmp");
            open.FileTypeFilter.Add(".png");
            open.FileTypeFilter.Add(".jpeg");
            open.FileTypeFilter.Add(".jpg");

            // Open a stream for the selected file
            StorageFile file = await open.PickSingleFileAsync();

            // Ensure a file was selected
            if (file != null)
            {
                var stream = await file.OpenAsync(FileAccessMode.Read);
                Windows.Storage.Streams.Buffer buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
                await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
            }
        }

        public ICommand AddPhotoCommand
        {
            get
            {
                return new RelayCommand(() => AddPhoto());
            }
        }

        #endregion

        public Activities()
        {
            DispatcherHelper.Initialize();
        }

        public AsyncActionCompletedHandler RunOnComplete(Action method)
        {
            return new AsyncActionCompletedHandler((IAsyncAction source, AsyncStatus status) =>
            {
                DispatcherHelper.CheckBeginInvokeOnUI(() => { method(); });
            });
        }

        public async Task UpdateData()
        {
            couple = AuthClient.GetUsers();
            activityList = AuthClient.GetActivities();
            listModelList = AuthClient.GetListModelList();
            calendarItems = AuthClient.GetCalendar();
            mediaList = AuthClient.GetMedia();
            return;
        }

        public void ProcessUpdate()
        {
            Couple = couple;
            ActivityList = activityList;
            ListModelList = listModelList;
            CalendarItems = calendarItems;
            MediaList = mediaList;
            foreach (var activity in ActivityList)
            {
                activity.User = activity.UserId == Couple.CurrentUser.Id ? Couple.CurrentUser : Couple.OtherUser;
            }
            ActivityList.Reverse();

            var tab = SelectedTab;
            var selectedList = SelectedListModel;

            SelectedActivity = ActivityList.First();
            SelectedListModel = selectedList;

            //restore the selected tab in case restoring the selected list caused this to change
            SelectedTab = tab;
            IsLoading = false;
        }

        public void Update()
        {
            IsLoading = true;
            var task = ThreadPool.RunAsync(t => UpdateData());

            task.Completed = RunOnComplete(ProcessUpdate);
        }


    }
}