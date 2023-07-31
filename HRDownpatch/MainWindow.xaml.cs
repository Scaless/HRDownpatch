using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HRDownpatch
{
    public struct DownloadDepotArgs
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string AppID { get; set; }
        public string DepotID { get; set; }
        public string TargetManifestID { get; set; }
        public Game GameType { get; set; }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        static readonly HttpClient client = new HttpClient();
        string LogText = "";
        string LogSteamCMDText = "";
        bool forceKillSteamCMD = false;

        InstallGroup? TargetInstallGroup = null;
        List<Manifest> TargetManifests = new List<Manifest>();

        private string _DownloadLocation;
        public string DownloadLocation { 
            get { return _DownloadLocation; }
            set { if (value != _DownloadLocation) { _DownloadLocation = value; OnPropertyChanged("DownloadLocation"); } }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _DownloadLocation = "";

            LogLine("=== HRDownpatch was started ===");
            LogLine(FreeSpaceReport());
        }

        void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogText += message;
                System.IO.File.AppendAllText("log.txt", message);
                tb_Log.Text = LogText;
            });
        }

        void LogLine(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogText += message;
                LogText += Environment.NewLine;
                System.IO.File.AppendAllText("log.txt", message);
                System.IO.File.AppendAllText("log.txt", Environment.NewLine);
                tb_Log.Text = LogText;
                sv_Log.ScrollToBottom();
            });
        }

        void LogSteamCMD(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogSteamCMDText += message;
                LogSteamCMDText += Environment.NewLine;
                System.IO.File.AppendAllText("steam.txt", message);
                System.IO.File.AppendAllText("steam.txt", Environment.NewLine);
                tb_LogSteam.Text = LogSteamCMDText;
                sv_LogSteam.ScrollToBottom();
            });
        }
        
        private string CreateTempDownloadScript(DownloadDepotArgs args)
        {
            string temp = System.IO.File.ReadAllText("scripts/download_template.txt");

            string output = string.Format(temp, args.Username, args.Password, args.AppID, args.DepotID, args.TargetManifestID);

            Random rng = new Random();
            string random = rng.NextInt64().ToString();

            string outFile = "scripts/temp_" + random + ".txt";

            System.IO.File.WriteAllText(outFile, output);

            return outFile;
        }

        private async Task LaunchSteamCMDPromptForLogin(string args)
        {
            var p = new Process();
            p.StartInfo.FileName = "steamcmd/steamcmd.exe";
            p.StartInfo.Arguments = args;
            p.StartInfo.CreateNoWindow = false;

            p.Start();

            await p.WaitForExitAsync();
        }

        private string BytesToHumanReadableBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
            // show a single decimal place, and no space.
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }

        private async Task<bool> LaunchSteamCMD(string args, DownloadDepotArgs downloadArgs)
        {
            bool success = true;

            var p = new Process();
            p.StartInfo.FileName = "steamcmd/steamcmd.exe";
            p.StartInfo.Arguments = args;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

            p.OutputDataReceived += (sender, args) => LogSteamCMD(args.Data ?? "");

            bool started = p.Start();
            if(started)
            {
                p.BeginOutputReadLine();

                long lastDirSize = 0;
                while (p.HasExited == false)
                {
                    if (forceKillSteamCMD)
                    {
                        p.Kill();
                        forceKillSteamCMD = false;
                        break;
                    }
                    else
                    {
                        await Task.Delay(5000);
                        string outputDir = string.Format("steamcmd/steamapps/content/app_{0}/depot_{1}", downloadArgs.AppID, downloadArgs.DepotID);

                        long dirSize = DirSize(new DirectoryInfo(outputDir));

                        if(dirSize != lastDirSize)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                tb_CurrentDownloadSize.Text = $"Download in progress... {BytesToHumanReadableBytes(dirSize)} written.";
                                tb_CurrentDownloadSize.Background = Brushes.LightGreen;
                            });

                            lastDirSize = dirSize;
                        }
                    }
                }

                await p.WaitForExitAsync();
            }
            LogSteamCMD($"*** steamcmd.exe has exited with exit code {p.ExitCode} ***");

            success = (p.ExitCode == 0);

            return success;
        }

        private async Task SetupSteamCMD()
        {
            try
            {
                if(System.IO.File.Exists("steamcmd/steamcmd.exe"))
                {
                    LogLine("steamcmd.exe already exists, skipping setup.");
                    return;
                }

                Log("Downloading steamcmd.zip ... ");
                byte[] zip = await client.GetByteArrayAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
                LogLine("Done");

                string localZipName = "steamcmd.zip";

                Log("Saving steamcmd.zip ... ");
                await System.IO.File.WriteAllBytesAsync(localZipName, zip);
                LogLine("Done");

                Log("Decompressing steamcmd.zip ... ");
                System.IO.Compression.ZipFile.ExtractToDirectory(localZipName, "steamcmd");
                LogLine("Done");

                Log("Cleaning up steamcmd.zip ... ");
                System.IO.File.Delete(localZipName);
                LogLine("Done");
            }
            catch (Exception ex)
            {
                LogLine(ex.Message);
                LogLine("*** Setup Failed ***");
            }
        }

        private bool ValidateDownloadOptions()
        {
            if (string.IsNullOrWhiteSpace(DownloadLocation))
            {
                System.Windows.MessageBox.Show("Download Location is required.", "", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return false;
            }
            if (string.IsNullOrWhiteSpace(tb_Username.Password))
            {
                System.Windows.MessageBox.Show("Steam Username is required.", "", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return false;
            }
            if (string.IsNullOrWhiteSpace(tb_Password.Password))
            {
                System.Windows.MessageBox.Show("Steam Password is required.", "", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return false;
            }
            if(TargetInstallGroup == null || TargetManifests.Count == 0)
            {
                System.Windows.MessageBox.Show("There are no depots selected.", "", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return false;
            }

            return true;
        }

        public async Task MoveDirectory(string sourceFolder, string destFolder)
        {
            if (!Directory.Exists(destFolder))
                Directory.CreateDirectory(destFolder);

            // Get Files & Move
            string[] files = Directory.GetFiles(sourceFolder);
            foreach (string file in files)
            {
                string name = System.IO.Path.GetFileName(file);
                string dest = System.IO.Path.Combine(destFolder, name);
                if(File.Exists(dest))
                {
                    File.Delete(dest);
                }
                File.Move(file, dest);

                // After each file move, defer some time to let the UI stay responsive.
                await Task.Delay(10);
            }

            // Get dirs recursively and move files
            string[] folders = Directory.GetDirectories(sourceFolder);
            foreach (string folder in folders)
            {
                string name = System.IO.Path.GetFileName(folder);
                string dest = System.IO.Path.Combine(destFolder, name);
                await MoveDirectory(folder, dest);
            }
        }

        private async Task CreateMCCSteamAppIDFile()
        {
            string appIDLocation = System.IO.Path.Combine(DownloadLocation, "steam_appid.txt");

            if (!File.Exists(appIDLocation))
            {
                await File.WriteAllTextAsync(appIDLocation, "976730");
            }
        }

        private async Task MoveFilesToDownloadLocation(string AppID, string DepotID)
        {
            string LocalPath = $"{System.IO.Path.GetDirectoryName(Environment.ProcessPath)}\\steamcmd\\steamapps\\content\\app_{AppID}\\depot_{DepotID}";
            
            await MoveDirectory(LocalPath, DownloadLocation);

            //foreach (string filename in Directory.EnumerateFiles(LocalPath))
            //{
            //    using (FileStream SourceStream = File.Open(filename, FileMode.Open))
            //    {
            //        using (FileStream DestinationStream = File.Create(DownloadLocation + filename.Substring(filename.LastIndexOf('\\'))))
            //        {
            //            await SourceStream.CopyToAsync(DestinationStream);
            //        }
            //    }
            //}

            
        }

        private async void btn_Start_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDownloadOptions())
            {
                return;
            }

            btn_Kill.IsEnabled = true;
            btn_Start.IsEnabled = false;
            btn_ChangeDownload.IsEnabled = false;
            btn_SelectDepots.IsEnabled = false;
            tb_Password.IsEnabled = false;
            tb_Username.IsEnabled = false;

            taskBarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;

            await SetupSteamCMD();

            Log("Configuring steam login ... ");
            string setupArgs = string.Format("+login {0} {1} +runscript ../scripts/setup.txt", tb_Username.Password, tb_Password.Password);
            await LaunchSteamCMDPromptForLogin(setupArgs);
            LogLine("Done");

            foreach (var Manifest in TargetManifests)
            {
                DownloadDepotArgs downloadArgs = new DownloadDepotArgs
                {
                    Username = tb_Username.Password,
                    Password = tb_Password.Password,
                    AppID = Manifest.AppID,
                    DepotID = Manifest.DepotID,
                    TargetManifestID = Manifest.ManifestID,
                };

                tb_CurrentDepot.Text = $"Current Depot: {Manifest.Name}";

                string downloadScript = CreateTempDownloadScript(downloadArgs);

                LogLine("Depot Download Starting!");
                string runArgs = "+runscript ../" + downloadScript;
                bool steamSuccess = await LaunchSteamCMD(runArgs, downloadArgs);
                LogLine("Depot Download Complete!");

                Log("Cleaning up download script ... ");
                System.IO.File.Delete(downloadScript);
                LogLine("Done");

                LogLine("Waiting for the dust to settle ...");
                System.Threading.Thread.Sleep(5000);

                if(steamSuccess)
                {
                    Log("Moving files from temporary storage to Download Location ...");
                    await MoveFilesToDownloadLocation(downloadArgs.AppID, downloadArgs.DepotID);
                    LogLine("Done");
                }
                else
                {
                    LogLine("Steam returned a non-0 return code, so we're not moving files.");
                }
            }

            if(TargetInstallGroup?.GameType == Game.MCC)
            {
                Log("Creating MCC steam_appid.txt ...");
                await CreateMCCSteamAppIDFile();
                LogLine("Done");
            }

            LogLine("All Depots Finished Downloading.");

            taskBarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;

            tb_CurrentDepot.Text = "";
            tb_CurrentDownloadSize.Text = "";
            tb_CurrentDownloadSize.Background = Brushes.White;

            btn_Kill.IsEnabled = false;
            btn_Start.IsEnabled = true;
            btn_ChangeDownload.IsEnabled = true;
            btn_SelectDepots.IsEnabled = true;
            tb_Password.IsEnabled = true;
            tb_Username.IsEnabled = true;
        }

        public static long DirSize(DirectoryInfo d)
        {
            try
            {
                long size = 0;
                // Add file sizes.
                FileInfo[] fis = d.GetFiles();
                foreach (FileInfo fi in fis)
                {
                    size += fi.Length;
                }
                // Add subdirectory sizes.
                DirectoryInfo[] dis = d.GetDirectories();
                foreach (DirectoryInfo di in dis)
                {
                    size += DirSize(di);
                }
                return size;
            }
            catch {
                return 0;
            }
        }

        private void btn_Kill_Click(object sender, RoutedEventArgs e)
        {
            string message = "Press YES to kill steamcmd." +
                Environment.NewLine +
                "Press NO to continue downloading." +
                Environment.NewLine + Environment.NewLine +
                "Downloads are NOT continuable, so cancelling will require starting over.";
            if(System.Windows.MessageBox.Show(message, "Kill steamcmd?", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                forceKillSteamCMD = true;
            }
        }

        private void btn_ChangeDownload_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                var result = fbd.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    {
                        FileInfo infoTarget = new FileInfo(fbd.SelectedPath);
                        string? driveTarget = System.IO.Path.GetPathRoot(infoTarget.FullName);

                        FileInfo infoDownload = new FileInfo(Environment.ProcessPath ?? "");
                        string? driveDownload = System.IO.Path.GetPathRoot(infoDownload.FullName);

                        if (driveTarget != driveDownload)
                        {
                            string message = $"The drive you selected ({driveTarget}) does not match the drive that this program is running from ({driveDownload})." +
                                Environment.NewLine + Environment.NewLine +
                                "It is ***highly*** recommended that you run HRDownpatch from the same drive that you set as your Download Location, " +
                                "otherwise an expensive copy will have to be performed when each download is complete." +
                                Environment.NewLine + Environment.NewLine +
                                "Press YES to close the program so you can move it." +
                                Environment.NewLine +
                                "Press NO if you're an idiot and want to use this drive anyways." +
                                Environment.NewLine +
                                "Press CANCEL to do nothing.";
                            var WarningResult = System.Windows.MessageBox.Show(message, "Drive Location Mismatch", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                            if (WarningResult == MessageBoxResult.Yes)
                            {
                                Close();
                                return;
                            }
                            else if(WarningResult == MessageBoxResult.Cancel)
                            {
                                return;
                            }
                        }
                    }

                    if (Directory.GetFiles(fbd.SelectedPath).Count() != 0)
                    {
                        string message = $"The chosen directory ({fbd.SelectedPath}) is not empty." + 
                            Environment.NewLine + Environment.NewLine +
                            "If you are downloading from scratch, the Download Location should usually be empty." + 
                            Environment.NewLine + Environment.NewLine +
                            "This can be OK if you already have the Base files for an MCC season and are just downloading an additional game." + 
                            Environment.NewLine + Environment.NewLine +
                            "Press YES to continue using this directory." +
                            Environment.NewLine +
                            "Press NO to cancel.";
                        if(System.Windows.MessageBox.Show(message, "Target Directory Not Empty", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    DownloadLocation = fbd.SelectedPath;
                }
            }
        }

        private string FreeSpaceReport()
        {
            string output = $"Available Free Space on Drives:{Environment.NewLine}";
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    output += $"{drive.Name}: {BytesToHumanReadableBytes(drive.AvailableFreeSpace)}{Environment.NewLine}";
                }
            }
            return output;
        }

        private long GetTotalFreeSpace(string driveName)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.Name == driveName)
                {
                    return drive.TotalFreeSpace;
                }
            }
            return -1;
        }

        private void btn_SelectDepots_Click(object sender, RoutedEventArgs e)
        {
            DepotSelector depotSelector = new DepotSelector();
            depotSelector.Owner = this;
            if (depotSelector.ShowDialog() == true)
            {
                TargetInstallGroup = depotSelector.OutInstallGroup;
                TargetManifests = depotSelector.OutManifests;

                if(TargetInstallGroup != null && TargetManifests.Count > 0)
                {
                    tb_SelectedDepots.Text = $"Selected ({TargetInstallGroup.Value.Name}) with {TargetManifests.Count} included depot(s).";
                }
                else
                {
                    tb_SelectedDepots.Text = "Select some depots!";
                }
            }
        }

        private void Window_GotFocus(object sender, RoutedEventArgs e)
        {
            if(taskBarItem.ProgressState == System.Windows.Shell.TaskbarItemProgressState.Normal)
            {
                taskBarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            }
        }
    }
}
