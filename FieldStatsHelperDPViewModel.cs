//Copyright 2017 Esri

//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at

//       http://www.apache.org/licenses/LICENSE-2.0

//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License. 

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Events;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.Data;
using MathNet.Numerics.Statistics;
using System;
using System.Globalization;
using System.Windows;

namespace FieldStatsHelper
{
    internal class FieldStatsHelperDPViewModel : DockPane
    {
        #region Private Properties
        private const string DockPaneId = "FieldStatsHelper_DockPane";

        /// <summary>
        /// used to lock collections for use by multiple threads
        /// </summary>
        private readonly object _lockMapCollections = new object();
        private readonly object _lockLayerCollections = new object();
        private readonly object _lockFieldCollections = new object();


        /// <summary>
        /// UI lists, readonly collections, and properties
        /// </summary>
        private readonly ObservableCollection<Map> _listOfMaps = new ObservableCollection<Map>();
        private readonly ObservableCollection<FeatureLayer> _listOfLayers = new ObservableCollection<FeatureLayer>();
        private readonly ObservableCollection<FieldDescription> _listOfFields = new ObservableCollection<FieldDescription>();

        private readonly ReadOnlyObservableCollection<Map> _readOnlyListOfMaps;
        private readonly ReadOnlyObservableCollection<FeatureLayer> _readOnlyListOfLayers;
        private readonly ReadOnlyObservableCollection<FieldDescription> _readOnlyListOfFields;

        private Map _selectedMap;
        private FeatureLayer _selectedLayer;
        private FieldDescription _selectedField;

        // Field stats
        private double _fieldMin = Double.NaN, _fieldMax = Double.NaN, _fieldMean = Double.NaN, 
                        _fieldMedian = Double.NaN, _fieldStdDev = Double.NaN;
        private int _fieldNulls = int.MinValue;

        private ICommand _retrieveMapsCommand;

        #endregion

        #region Public Properties

        /// <summary>
        /// Our List of Maps which is bound to our Dockpane XAML
        /// </summary>
        public ReadOnlyObservableCollection<Map> ListOfMaps => _readOnlyListOfMaps;

        /// <summary>
        /// Our list of layers, bound to the UI XAML
        /// </summary>
        public ReadOnlyObservableCollection<FeatureLayer> ListOfLayers => _readOnlyListOfLayers;
        
        /// <summary>
        /// Our List of fieldDescriptions which is bound to our Dockpane XAML
        /// </summary>
        public ReadOnlyObservableCollection<FieldDescription> ListOfFields => _readOnlyListOfFields;

        /// <summary>
        /// Data for the histogram
        /// </summary>
        public List<KeyValuePair<double, int>> _chartData; // = new ObservableCollection<KeyValuePair<int, double>>();
        public List<KeyValuePair<double, int>> ChartData {
            get { return _chartData; }
            set {
                SetProperty(ref _chartData, value, () => ChartData);
            }
        }

        public string ChartTitle {
            get {
                return "Histogram\n[" + SelectedField?.Name + "]";
            }
        }
        /// <summary>
        /// This is where we store the selected map 
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Await.Warning", "CS4014:Await.Warning")]
        public Map SelectedMap {
            get { return _selectedMap; }
            set {
                System.Diagnostics.Debug.WriteLine("selected map");
                // make sure we're on the UI thread
                Utils.RunOnUIThread(() => {
                    SetProperty(ref _selectedMap, value, () => SelectedMap);
                    if (_selectedMap != null) {
                        // open /activate the map
                        Utils.OpenAndActivateMap(_selectedMap.URI);
                    }
                });
                System.Diagnostics.Debug.WriteLine("selected map opened and activated map");
                // no need to await
                UpdateLayers(_selectedMap);
                System.Diagnostics.Debug.WriteLine("updated layers");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Await.Warning", "CS4014:Await.Warning")]
        public FeatureLayer SelectedLayer {
            get { return _selectedLayer; }
            set {
                SetProperty(ref _selectedLayer, value, () => SelectedLayer);
                System.Diagnostics.Debug.WriteLine("selected layer");
                if (_selectedLayer != null) {
                    // Get fields in the feature layer
                    QueuedTask.Run(() => {
                        UpdateFields(_selectedLayer);
                    });
                }
            }
        }

        /// <summary>
        /// This is where we store the selected Field 
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Await.Warning", "CS4014:Await.Warning")]
        public FieldDescription SelectedField {
            get { return _selectedField; }
            set {
                SetProperty(ref _selectedField, value, () => SelectedField);
                System.Diagnostics.Debug.WriteLine("selected field");
                if (_selectedField != null) {
                    UpdateFieldStats();
                }
                NotifyPropertyChanged("ChartTitle");
                System.Diagnostics.Debug.WriteLine("Selected field changed");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Await.Warning", "CS4014:Await.Warning")]
        private async Task UpdateFieldStats() {
            object _lockValueList = new object();
            List<double> values = new List<double>();
            List<long> nulls = new List<long>(); // OIDs of null values
            List<long> errors = new List<long>(); // OIDs of non-numeric values
            // GetMap needs to be on the MCT
            QueuedTask.Run(() => {
                // Get and display statistics for the field
                QueryFilter qf = new QueryFilter();
                qf.SubFields = _selectedField.Name;
                RowCursor rc = SelectedLayer.GetTable().Search(qf);
                int iField = rc.FindField(_selectedField.Name);
                lock (_lockValueList) {
                    while (rc.MoveNext()) {
                        object value = rc.Current.GetOriginalValue(iField);
                        if (value != DBNull.Value)
                            try {
                                values.Add(Convert.ToDouble(value));
                            } catch {
                                errors.Add(rc.Current.GetObjectID()); // Shouldn't hit this
                            } else // Note the null value
                            nulls.Add(long.MinValue);
                    }
                }
                FieldMedian = Statistics.Median(values);
                FieldMean = Statistics.Mean(values);
                FieldMin = Statistics.Minimum(values);
                FieldMax = Statistics.Maximum(values);
                FieldStdDev = Statistics.StandardDeviation(values);
                FieldNulls = nulls.Count;

                Histogram histogram = new Histogram(values, 25);
                ChartData = new List<KeyValuePair<double, int>>();
                for (int i = 0; i < histogram.BucketCount; i++) {
                    ChartData.Add(new KeyValuePair<double, int>(histogram[i].UpperBound, (int)histogram[i].Count));
                }
            });
        }

        public double FieldMin {
            get {
                return _fieldMin;
            }

            set {
                SetProperty(ref _fieldMin, value, () => FieldMin);
            }
        }

        public double FieldMax {
            get {
                return _fieldMax;
            }

            set {
                SetProperty(ref _fieldMax, value, () => FieldMax);
            }
        }

        public double FieldMean {
            get {
                return _fieldMean;
            }

            set {
                SetProperty(ref _fieldMean, value, () => FieldMean);
            }
        }

        public double FieldMedian {
            get {
                return _fieldMedian;
            }

            set {
                SetProperty(ref _fieldMedian, value, () => FieldMedian);
            }
        }

        public double FieldStdDev {
            get {
                return _fieldStdDev;
            }

            set {
                SetProperty(ref _fieldStdDev, value, () => FieldStdDev);
            }
        }

        public int FieldNulls {
            get {
                return _fieldNulls;
            }

            set {
                SetProperty(ref _fieldNulls, value, () => FieldNulls);
            }
        }

        /// <summary>
        /// Implement a 'RelayCommand' to retrieve all maps from the current project
        /// </summary>
        public ICommand RetrieveMapsCommand => _retrieveMapsCommand;

        #endregion

        #region CTor

        protected FieldStatsHelperDPViewModel()
        {
            // setup the lists and sync between background and UI
            _readOnlyListOfMaps = new ReadOnlyObservableCollection<Map>(_listOfMaps);
            _readOnlyListOfLayers = new ReadOnlyObservableCollection<FeatureLayer>(_listOfLayers);
            _readOnlyListOfFields = new ReadOnlyObservableCollection<FieldDescription>(_listOfFields);
            BindingOperations.EnableCollectionSynchronization(_readOnlyListOfMaps, _lockMapCollections);
            BindingOperations.EnableCollectionSynchronization(_readOnlyListOfLayers, _lockLayerCollections);
            BindingOperations.EnableCollectionSynchronization(_readOnlyListOfFields, _lockFieldCollections);

            // set up the command to retrieve the maps
            _retrieveMapsCommand = new RelayCommand(() => RetrieveMaps(), () => true);
        }

        #endregion
        
        #region Overrides

        /// <summary>
        /// Override to implement custom initialization code for this dockpane
        /// </summary>
        /// <returns></returns>
        protected override Task InitializeAsync()
        {
            ProjectItemsChangedEvent.Subscribe(OnProjectCollectionChanged, false);
            return base.InitializeAsync();
        }
        #endregion

        #region Show dockpane 
        /// <summary>
        /// Show the DockPane.
        /// </summary>
        internal static void Show()
        {
            var pane = FrameworkApplication.DockPaneManager.Find(DockPaneId);
            pane?.Activate();
        }

        /// <summary>
        /// Text shown near the top of the DockPane.
        /// </summary>
        private string _heading = "Maps and Bookmarks";
        public string Heading
        {
            get { return _heading; }
            set
            {
                SetProperty(ref _heading, value, () => Heading);
            }
        }

        #endregion Show dockpane 

        #region Subscribed Events

        /// <summary>
        /// Subscribe to Project Items Changed events which is getting called each
        /// time the project items change which happens when a new map is added or removed in ArcGIS Pro
        /// </summary>
        /// <param name="args">ProjectItemsChangedEventArgs</param>
        private void OnProjectCollectionChanged(ProjectItemsChangedEventArgs args)
        {
            if (args == null)
                return;
            var mapItem = args.ProjectItem as MapProjectItem;
            if (mapItem == null)
                return;

            // new project item was added
            switch (args.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    {
                        var foundItem = _listOfMaps.FirstOrDefault(m => m.URI == mapItem.Path);
                        // one cannot be found; so add it to our list
                        if (foundItem == null)
                        {
                            _listOfMaps.Add(mapItem.GetMap());
                        }
                    }
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    {
                        Map map = mapItem.GetMap();
                        // if this is the selected map, resest
                        if (SelectedMap == map)
                            SelectedMap = null;

                        // remove from the collection
                        if (_listOfMaps.Contains(map))
                        {
                            _listOfMaps.Remove(map);
                        }
                    }
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset: {
                        _listOfMaps.Clear();
                    }
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace: {
                        System.Diagnostics.Debug.WriteLine("Maps list replace");
                    }
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine("Maps list other");
                    break;
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Method for retrieving map items in the project.
        /// </summary>
        private async void RetrieveMaps()
        {
            System.Diagnostics.Debug.WriteLine("RetrieveMaps");
            // clear the collections
            _listOfMaps.Clear();
            System.Diagnostics.Debug.WriteLine("RetrieveMaps list of maps clear");
            if (Project.Current != null)
            {
                System.Diagnostics.Debug.WriteLine("RetrieveMaps add maps");
                // GetMap needs to be on the MCT
                await QueuedTask.Run(() =>
                {
                    // get the map project items and add to my collection
                    foreach (MapProjectItem item in Project.Current.GetItems<MapProjectItem>())
                    {
                        _listOfMaps.Add(item.GetMap());
                    }
                });
            }
            System.Diagnostics.Debug.WriteLine("RetrieveMaps added maps");
        }

        private async Task UpdateLayers(Map map) {
            // get the layers.  GetLayers needs to be on MCT but want to refresh members and properties on UI thread

            System.Diagnostics.Debug.WriteLine("UpdateLayers");
            _listOfLayers.Clear();
            System.Diagnostics.Debug.WriteLine("UpdateLayers list cleared");
            if (map == null)
            {
                System.Diagnostics.Debug.WriteLine("RetrieveMaps no maps");
                return;
            }
            await QueuedTask.Run(() =>
            {
                foreach (var layer in map.GetLayersAsFlattenedList()) {
                    if (layer is FeatureLayer) _listOfLayers.Add((FeatureLayer) layer);
                }
            });
            System.Diagnostics.Debug.WriteLine("UpdateLayers new list done");
        }

        private async Task UpdateFields(FeatureLayer layer) {
            System.Diagnostics.Debug.WriteLine("UpdateFields");
            _listOfFields.Clear();
            System.Diagnostics.Debug.WriteLine("UpdateFields list cleared");
            if (layer == null) {
                System.Diagnostics.Debug.WriteLine("No feature layer selected");
                return;
            }
            await QueuedTask.Run(() => {
                foreach (var field in layer.GetFieldDescriptions()) {
                    if (field.Type == FieldType.Integer || field.Type == FieldType.Single || field.Type == FieldType.Double || field.Type == FieldType.SmallInteger) {
                        _listOfFields.Add(field);
                    }
                }
            });
        }

        #endregion Private Helpers
    }

    /// <summary>
    /// Button implementation to show the DockPane.
    /// </summary>
    internal class FieldStatsHelperDP_ShowButton : Button
    {
        protected override void OnClick() {
            FieldStatsHelperDPViewModel.Show();
        }
    }


    [ValueConversion(typeof(double), typeof(string))]
    public class FieldStatDoubleToStringConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value.GetType() == typeof(double) && Double.IsNaN((double)value))
                return String.Empty;
            else return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }
    [ValueConversion(typeof(int), typeof(string))]
    public class FieldNullIntToStringConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value.GetType() == typeof(int) && (int)value < 0)
                return String.Empty;
            else return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }

    [ValueConversion(typeof(List<KeyValuePair<double, int>>), typeof(Visibility))]
    public class ChartVisibilityDataAvailableConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value != null
                && (value.GetType() == typeof(List<KeyValuePair<double, int>>))
                && ((List<KeyValuePair<double, int>>)value).Count > 0) {
                    return Visibility.Visible;
            }
            else return Visibility.Collapsed;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
