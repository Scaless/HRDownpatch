using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HRDownpatch
{
    public class ManifestGridItem
    {
        public bool Install { get; set; }
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public UInt64 SizeInBytes { get; set; }
        public string? ManifestID { get; set; }
    }

    /// <summary>
    /// Interaction logic for DepotSelector.xaml
    /// </summary>
    public partial class DepotSelector : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        DepotManifest? DepotManifest;

        public List<string> InstallGroups { get; set; } = new List<string>();
        public string? SelectedInstallGroupName { get; set; }
        //public InstallGroup? SelectedInstallGroup { get; set; }

        private InstallGroup? _SelectedInstallGroup;
        public InstallGroup? SelectedInstallGroup
        {
            get { return _SelectedInstallGroup; }
            set { _SelectedInstallGroup = value; OnPropertyChanged("SelectedInstallGroup"); }
        }

        public InstallGroup? OutInstallGroup = null;
        public List<Manifest> OutManifests = new List<Manifest>();

        public ObservableCollection<ManifestGridItem> ManifestGridItems { get; set; } = new ObservableCollection<ManifestGridItem>();

        public DepotSelector()
        {
            InitializeComponent();
            DataContext = this;
            RefreshManifests();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RefreshManifests()
        {
            const string local_manifest = "C:\\Repos\\HRDownpatch\\depot_manifest.json";
            if (System.IO.File.Exists(local_manifest))
            {
                DepotManifest = JsonConvert.DeserializeObject<DepotManifest>(System.IO.File.ReadAllText(local_manifest));
            }
            else
            {
                // HTTP Get current manifest
                const string manifest_url = "https://raw.githubusercontent.com/Scaless/HRDownpatch/main/depot_manifest.json";
                HttpClient client = new HttpClient();
                HttpResponseMessage manifest_response = client.GetAsync(manifest_url).Result;
                if (!manifest_response.IsSuccessStatusCode)
                {
                    string caption = "HRDownpatch Manifest Could Not Be Downloaded";
                    string message = "Couldn't download the depot manifest.";
                    System.Windows.MessageBox.Show(message, caption, MessageBoxButton.OK);
                    return;
                }

                DepotManifest = JsonConvert.DeserializeObject<DepotManifest>(manifest_response.Content.ReadAsStringAsync().Result);
            }

            // Is Manifest OK?
            if (DepotManifest?.InstallGroups == null)
            {
                string caption = "HRDownpatch Manifest Corrupted";
                string message = "Failed to read the depot manifest.";
                System.Windows.MessageBox.Show(message, caption, MessageBoxButton.OK);
                return;
            }

            // Refresh the UI with the new manifest
            InstallGroups.Clear();
            foreach (InstallGroup group in DepotManifest.InstallGroups)
            {
                InstallGroups.Add(group.Name);
            }
        }

        private void RefreshGrid()
        {
            ManifestGridItems.Clear();

            if (DepotManifest?.InstallGroups == null) { return; }

            InstallGroup? nGroup = DepotManifest?.InstallGroups?.FirstOrDefault(x => x.Name == SelectedInstallGroupName);

            if (nGroup == null)
                return;

            InstallGroup group = nGroup.Value;
            List<string> GroupDepots = new List<string>
            {
                group.Depot1,
                group.Depot2,
                group.Depot3,
                group.Depot4,
                group.Depot5,
                group.Depot6,
                group.Depot7,
                group.Depot8,
            };
            GroupDepots.RemoveAll(string.IsNullOrEmpty);

            foreach(var depot in GroupDepots)
            {
                Manifest? nManifest = DepotManifest?.Manifests?.FirstOrDefault(x => x.Slug == depot);

                if (nManifest == null)
                    continue;

                Manifest manifest = nManifest.Value;
                
                ManifestGridItem newItem = new ManifestGridItem
                {
                    Install = (manifest.Slug.Contains("MCCBase")) ? true : false,
                    Name = manifest.Name,
                    Slug = manifest.Slug,
                    SizeInBytes = manifest.TotalSizeBytes,
                    ManifestID = manifest.ManifestID.Replace("_", "")
                };
                ManifestGridItems.Add(newItem);
            }

        }

        private bool ValidateSelections()
        {
            if (SelectedInstallGroupName == null)
            {
                return false;
            }

            return true;
        }

        private void btn_Reload_Click(object sender, RoutedEventArgs e)
        {
            RefreshManifests();
        }

        private void cb_InstallGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedInstallGroup = DepotManifest?.InstallGroups?.FirstOrDefault(x => x.Name == SelectedInstallGroupName);
            RefreshGrid();
        }

        private void btn_OK_Click(object sender, RoutedEventArgs e)
        {
            if(ValidateSelections())
            {
                OutInstallGroup = DepotManifest?.InstallGroups?.FirstOrDefault(x => x.Name.Equals(SelectedInstallGroupName));
                
                if(OutInstallGroup != null)
                {
                    foreach (var Manifest in ManifestGridItems)
                    {
                        if (Manifest.Install)
                        {
                            Manifest? nManifest = DepotManifest?.Manifests?.FirstOrDefault(x => x.Slug == Manifest.Slug);
                            if (nManifest == null)
                                continue;
                            Manifest manifest = nManifest.Value;
                            if(manifest.Slug != "")
                            {
                                // TODO: Fix up the underscores here for now, not the greatest place to do it
                                manifest.AppID = manifest.AppID.Replace("_", "");
                                manifest.DepotID = manifest.DepotID.Replace("_", "");
                                manifest.ManifestID = manifest.ManifestID.Replace("_", "");

                                OutManifests.Add(manifest);
                            }
                        }
                        else if (Manifest.Slug.Contains("MCCBase"))
                        {
                            string message = "The selected installation has a Base depot available but it is not selected to install. The Base depot MUST be installed to run any games. " +
                                Environment.NewLine + Environment.NewLine +
                                "Base depots are NOT interchangable! The version of the Base depot must match the games that are installed with it." +
                                Environment.NewLine + Environment.NewLine +
                                "If you have already downloaded this Base depot at the Download Location and are just downloading additional games, you may press OK to ignore this message.";
                            if(MessageBox.Show(message, "Base Depot Not Selected", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                            {
                                return;
                            }
                        }
                    }
                }

                if(OutInstallGroup == null ||  OutManifests.Count == 0)
                {
                    return;
                }

                DialogResult = true;
                Close();
            }
        }

        private void btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            OutInstallGroup = null;
            OutManifests.Clear();
            DialogResult = false;
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // for .NET Core you need to add UseShellExecute = true
            // see https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.useshellexecute#property-value
            using (Process compiler = new Process())
            {
                compiler.StartInfo.FileName = e.Uri.AbsoluteUri;
                compiler.StartInfo.UseShellExecute = true;
                compiler.Start();
            }
            e.Handled = true;
        }
    }
}
