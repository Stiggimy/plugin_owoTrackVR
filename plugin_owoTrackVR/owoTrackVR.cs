// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using System.Collections.Generic;
using System.Linq;
using Amethyst.Plugins.Contract;
using DeviceHandler;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using DQuaternion = DeviceHandler.Quaternion;
using DVector = DeviceHandler.Vector;
using Quaternion = System.Numerics.Quaternion;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace plugin_OwoTrack;

/// <summary>
/// Role options for each owoTrack device.
/// Disabled means the tracker won't create a joint in Amethyst.
/// </summary>
public enum TrackerRole
{
    Disabled,
    Waist,
    Chest,
    LeftFoot,
    RightFoot,
    LeftKnee,
    RightKnee,
    LeftElbow,
    RightElbow,
    Manual // User assigns in Amethyst
}

[Export(typeof(ITrackingDevice))]
[ExportMetadata("Name", "owoTrackVR")]
[ExportMetadata("Guid", "K2VRTEAM-AME2-APII-DVCE-DVCEOWOTRACK")]
[ExportMetadata("Publisher", "K2VR Team")]
[ExportMetadata("Version", "2.1.0.0")]
[ExportMetadata("Website", "https://github.com/KinectToVR/plugin_owoTrackVR")]
[ExportMetadata("DependencyLink", "https://docs.k2vr.tech/{0}/owo/about/")]
[ExportMetadata("CoreSetupData", typeof(SetupData))]
public class OwoTrack : ITrackingDevice
{
    // Update settings UI
    private int _statusBackup = (int)HandlerStatus.ServiceNotStarted;
    private int _lastTrackerCount = 0;

    // Per-tracker settings
    private class TrackerSettings
    {
        public TrackerRole Role { get; set; } = TrackerRole.Disabled;
        public uint TrackerHeightOffset { get; set; } = 75;
        public Quaternion GlobalRotation { get; set; } = Quaternion.Identity;
        public Quaternion LocalRotation { get; set; } = Quaternion.Identity;
    }
    private Dictionary<int, TrackerSettings> _trackerSettings = new();

    // Maps TrackedJoint index to tracker ID
    private Dictionary<int, int> _jointToTrackerMap = new();

    // Role display names for UI
    private static readonly Dictionary<TrackerRole, string> RoleDisplayNames = new()
    {
        { TrackerRole.Disabled, "Disabled" },
        { TrackerRole.Waist, "Waist" },
        { TrackerRole.Chest, "Chest" },
        { TrackerRole.LeftFoot, "Left Foot" },
        { TrackerRole.RightFoot, "Right Foot" },
        { TrackerRole.LeftKnee, "Left Knee" },
        { TrackerRole.RightKnee, "Right Knee" },
        { TrackerRole.LeftElbow, "Left Elbow" },
        { TrackerRole.RightElbow, "Right Elbow" },
        { TrackerRole.Manual, "Manual" }
    };

    // Role to TrackedJointType mapping
    // Using JointManual for all - user assigns body part in Amethyst's joint assignment UI
    // This provides maximum flexibility and avoids enum compatibility issues
    private static TrackedJointType RoleToJointType(TrackerRole role) => TrackedJointType.JointManual;

    public OwoTrack()
    {
        // Set up a new server update timer
        var timer = new Timer
        {
            Interval = 25, AutoReset = true, Enabled = true
        };
        timer.Elapsed += (_, _) =>
        {
            if (!PluginLoaded || !Handler.IsInitialized) return;
            Handler.Update(); // Sanity check, refresh the server
            
            // Check for new trackers and update TrackedJoints
            UpdateTrackedJointsIfNeeded();
        };
        timer.Start(); // Start the timer
    }

    [Import(typeof(IAmethystHost))] private IAmethystHost Host { get; set; }

    private TrackingHandler Handler { get; } = new();
    private bool CalibrationPending { get; set; }

    private Vector3 GlobalOffset { get; } = Vector3.Zero;
    private Vector3 DeviceOffset { get; } = new(0f, -0.045f, 0.09f);

    private Vector3 GetTrackerOffset(int trackerId)
    {
        var settings = GetOrCreateTrackerSettings(trackerId);
        return new Vector3(0f, settings.TrackerHeightOffset / -100f, 0f);
    }

    private bool PluginLoaded { get; set; }
    private Page InterfaceRoot { get; set; }

    public bool IsSkeletonTracked => TrackedJoints.Count > 0;
    public bool IsPositionFilterBlockingEnabled => false;
    public bool IsPhysicsOverrideEnabled => false;
    public bool IsSelfUpdateEnabled => false;
    public bool IsFlipSupported => false;
    public bool IsAppOrientationSupported => false;
    public bool IsSettingsDaemonSupported => true;
    public object SettingsInterfaceRoot => InterfaceRoot;

    public ObservableCollection<TrackedJoint> TrackedJoints { get; } = new();

    public int DeviceStatus
    {
        get
        {
            UpdateSettingsInterface();
            return Handler.StatusResult;
        }
    }

    public Uri ErrorDocsUri => new($"https://docs.k2vr.tech/{Host?.DocsLanguageCode ?? "en"}/owo/setup/");

    public bool IsInitialized => Handler.IsInitialized;

    public string DeviceStatusString => PluginLoaded
        ? DeviceStatus switch
        {
            (int)HandlerStatus.ServiceNotStarted => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NotStarted"),
            (int)HandlerStatus.ServiceSuccess => Host.RequestLocalizedString("/Plugins/OWO/Statuses/Success") + 
                $" ({Handler.TrackerCount} device{(Handler.TrackerCount != 1 ? "s" : "")}, {TrackedJoints.Count} active)",
            (int)HandlerStatus.ConnectionDead => Host.RequestLocalizedString("/Plugins/OWO/Statuses/ConnectionDead"),
            (int)HandlerStatus.ErrorNoData => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NoData"),
            (int)HandlerStatus.ErrorInitFailed => Host.RequestLocalizedString("/Plugins/OWO/Statuses/InitFailure"),
            (int)HandlerStatus.ErrorPortsTaken => Host.RequestLocalizedString("/Plugins/OWO/Statuses/PortsTaken"),
            _ => $"Status: {DeviceStatus}"
        }
        : Host?.RequestLocalizedString("/Plugins/OWO/Statuses/NotStarted") ?? "Not Started";

    private TrackerSettings GetOrCreateTrackerSettings(int trackerId)
    {
        if (!_trackerSettings.ContainsKey(trackerId))
        {
            _trackerSettings[trackerId] = new TrackerSettings
            {
                Role = (TrackerRole)Host.PluginSettings.GetSetting($"Tracker{trackerId}_Role", (int)TrackerRole.Disabled),
                TrackerHeightOffset = Host.PluginSettings.GetSetting($"Tracker{trackerId}_TrackerHeightOffset", 75U),
                GlobalRotation = Host.PluginSettings.GetSetting($"Tracker{trackerId}_GlobalRotation", Quaternion.Identity),
                LocalRotation = Host.PluginSettings.GetSetting($"Tracker{trackerId}_LocalRotation", Quaternion.Identity)
            };
            
            // Fix invalid values
            var settings = _trackerSettings[trackerId];
            if (settings.TrackerHeightOffset is < 60 or > 90) settings.TrackerHeightOffset = 75;
            if (settings.GlobalRotation == Quaternion.Zero) settings.GlobalRotation = Quaternion.Identity;
            if (settings.LocalRotation == Quaternion.Zero) settings.LocalRotation = Quaternion.Identity;
        }
        return _trackerSettings[trackerId];
    }

    private void SaveTrackerSettings(int trackerId)
    {
        var settings = GetOrCreateTrackerSettings(trackerId);
        Host.PluginSettings.SetSetting($"Tracker{trackerId}_Role", (int)settings.Role);
        Host.PluginSettings.SetSetting($"Tracker{trackerId}_TrackerHeightOffset", settings.TrackerHeightOffset);
        Host.PluginSettings.SetSetting($"Tracker{trackerId}_GlobalRotation", settings.GlobalRotation);
        Host.PluginSettings.SetSetting($"Tracker{trackerId}_LocalRotation", settings.LocalRotation);
    }

    private void MigrateOldSettings()
    {
        // Check if old-style settings exist and migrate to Tracker0_* format
        var oldGlobalRotation = Host.PluginSettings.GetSetting<Quaternion?>("GlobalRotation", null);
        if (oldGlobalRotation.HasValue)
        {
            Host.Log("Migrating old single-device settings to multi-device format...");
            
            var oldLocal = Host.PluginSettings.GetSetting("LocalRotation", Quaternion.Identity);
            var oldHeight = Host.PluginSettings.GetSetting("TrackerHeightOffset", 75U);
            
            Host.PluginSettings.SetSetting("Tracker0_GlobalRotation", oldGlobalRotation.Value);
            Host.PluginSettings.SetSetting("Tracker0_LocalRotation", oldLocal);
            Host.PluginSettings.SetSetting("Tracker0_TrackerHeightOffset", oldHeight);
            Host.PluginSettings.SetSetting("Tracker0_Role", (int)TrackerRole.Waist); // Default first tracker to Waist
            
            // Clear old settings by setting to default (can't actually delete)
            Host.PluginSettings.SetSetting("GlobalRotation", Quaternion.Identity);
            Host.PluginSettings.SetSetting("LocalRotation", Quaternion.Identity);
            Host.PluginSettings.SetSetting("TrackerHeightOffset", 75U);
            Host.PluginSettings.SetSetting("SettingsMigrated", true);
            
            Host.Log("Settings migration complete.");
        }
    }

    private void UpdateTrackedJointsIfNeeded()
    {
        if (!PluginLoaded || !Handler.IsInitialized) return;
        
        var currentCount = Handler.TrackerCount;
        
        // Rebuild joints if tracker count changed or we need to refresh
        if (currentCount != _lastTrackerCount)
        {
            Host.Log($"Tracker count changed from {_lastTrackerCount} to {currentCount}");
            _lastTrackerCount = currentCount;
            RebuildTrackedJoints();
        }
    }

    private void RebuildTrackedJoints()
    {
        TrackedJoints.Clear();
        _jointToTrackerMap.Clear();
        
        for (int trackerId = 0; trackerId < Handler.TrackerCount; trackerId++)
        {
            var settings = GetOrCreateTrackerSettings(trackerId);
            
            // Skip disabled trackers
            if (settings.Role == TrackerRole.Disabled) continue;
            
            var jointIndex = TrackedJoints.Count;
            _jointToTrackerMap[jointIndex] = trackerId;
            
            TrackedJoints.Add(new TrackedJoint
            {
                Name = $"owoTrack {trackerId} ({RoleDisplayNames[settings.Role]})",
                Role = RoleToJointType(settings.Role)
            });
            
            // Initialize handler with saved calibration
            Handler.SetGlobalRotation(trackerId, settings.GlobalRotation.ToWin());
            Handler.SetLocalRotation(trackerId, settings.LocalRotation.ToWin());
        }
        
        // Update UI if needed
        RebuildTrackerSettingsUI();
    }

    public void OnLoad()
    {
        // Migrate old settings if needed
        MigrateOldSettings();

        // Re-register native action handlers
        Handler.StatusChanged -= StatusChangedEventHandler;
        Handler.StatusChanged += StatusChangedEventHandler;
        Handler.LogEvent -= LogMessageEventHandler;
        Handler.LogEvent += LogMessageEventHandler;

        // Tell the handler to initialize
        if (!PluginLoaded) Handler.OnLoad();

        // Build the settings UI
        BuildSettingsInterface();

        // Mark the plugin as loaded
        PluginLoaded = true;
        UpdateSettingsInterface(true);
    }

    private StackPanel TrackersPanel { get; set; }
    private List<TrackerUIRow> _trackerUIRows = new();

    private class TrackerUIRow
    {
        public int TrackerId { get; set; }
        public StackPanel Container { get; set; }
        public TextBlock Label { get; set; }
        public ComboBox RoleSelector { get; set; }
        public Button CalibrateButton { get; set; }
        public NumberBox HeightBox { get; set; }
    }

    private void BuildSettingsInterface()
    {
        IpTextBlock = new TextBlock
        {
            Text = Handler.IP.Length > 1 // Format as list if found multiple IPs!
                ? $"[ {string.Join(", ", Handler.IP)} ]" // Or show a placeholder
                : Handler.IP.GetValue(0)?.ToString() ?? "127.0.0.1",
            Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
        };
        IpLabelTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString(Handler.IP.Length > 1
                ? "/Plugins/OWO/Settings/Labels/LocalIP/Multiple"
                : "/Plugins/OWO/Settings/Labels/LocalIP/One"),
            Margin = new Thickness(3), Opacity = 0.5
        };

        PortTextBlock = new TextBlock
        {
            Text = Handler.Port.ToString(), // Don't allow any changes
            Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
        };
        PortLabelTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Labels/Port"),
            Margin = new Thickness(3), Opacity = 0.5
        };

        MessageTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/NotStarted"),
            Margin = new Thickness(3), Opacity = 0.5
        };
        CalibrationTextBlock = new TextBlock { Visibility = Visibility.Collapsed };

        TrackersPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness { Top = 10, Bottom = 10 }
        };

        InterfaceRoot = new Page
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { IpLabelTextBlock, IpTextBlock }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { PortLabelTextBlock, PortTextBlock },
                        Margin = new Thickness { Bottom = 10 }
                    },
                    new TextBlock
                    {
                        Text = "Connected Devices:",
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(3)
                    },
                    TrackersPanel,
                    MessageTextBlock,
                    CalibrationTextBlock
                }
            }
        };
    }

    private void RebuildTrackerSettingsUI()
    {
        if (TrackersPanel == null) return;
        
        TrackersPanel.Children.Clear();
        _trackerUIRows.Clear();
        
        if (Handler.TrackerCount == 0)
        {
            TrackersPanel.Children.Add(new TextBlock
            {
                Text = "No devices connected. Open owoTrack on your phone and connect to this IP.",
                Opacity = 0.5,
                Margin = new Thickness(3),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        
        for (int trackerId = 0; trackerId < Handler.TrackerCount; trackerId++)
        {
            var settings = GetOrCreateTrackerSettings(trackerId);
            var tid = trackerId; // Capture for lambda
            
            var label = new TextBlock
            {
                Text = $"owoTrack {trackerId}:",
                VerticalAlignment = VerticalAlignment.Center,
                Width = 100,
                Margin = new Thickness(3)
            };
            
            var roleSelector = new ComboBox
            {
                MinWidth = 120,
                Margin = new Thickness { Right = 10 }
            };
            foreach (var role in Enum.GetValues<TrackerRole>())
            {
                roleSelector.Items.Add(RoleDisplayNames[role]);
            }
            roleSelector.SelectedIndex = (int)settings.Role;
            roleSelector.SelectionChanged += (s, e) =>
            {
                var newRole = (TrackerRole)roleSelector.SelectedIndex;
                var trackerSettings = GetOrCreateTrackerSettings(tid);
                trackerSettings.Role = newRole;
                SaveTrackerSettings(tid);
                RebuildTrackedJoints();
                Host.PlayAppSound(SoundType.Invoke);
            };
            
            var calibrateButton = new Button
            {
                Content = "Calibrate",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness { Right = 5 }
            };
            calibrateButton.Click += async (s, e) => await CalibrateTrackerAsync(tid);
            
            var identifyButton = new Button
            {
                Content = "📳",
                Margin = new Thickness { Right = 10 }
            };
            ToolTipService.SetToolTip(identifyButton, "Identify (vibrate)");
            identifyButton.Click += (s, e) =>
            {
                Handler.SignalTracker(tid);
                Host.PlayAppSound(SoundType.Invoke);
            };
            
            var heightLabel = new TextBlock
            {
                Text = "Height:",
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
                Margin = new Thickness { Right = 5 }
            };
            
            var heightBox = new NumberBox
            {
                Value = settings.TrackerHeightOffset,
                Minimum = 60,
                Maximum = 90,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 80
            };
            heightBox.ValueChanged += (sender, _) =>
            {
                if (double.IsNaN(sender.Value)) sender.Value = 75;
                sender.Value = Math.Clamp(sender.Value, 60, 90);
                
                var trackerSettings = GetOrCreateTrackerSettings(tid);
                trackerSettings.TrackerHeightOffset = (uint)sender.Value;
                SaveTrackerSettings(tid);
                Host.PlayAppSound(SoundType.Invoke);
            };
            
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness { Bottom = 8 },
                Children = { label, roleSelector, identifyButton, calibrateButton, heightLabel, heightBox }
            };
            
            TrackersPanel.Children.Add(row);
            _trackerUIRows.Add(new TrackerUIRow
            {
                TrackerId = trackerId,
                Container = row,
                Label = label,
                RoleSelector = roleSelector,
                CalibrateButton = calibrateButton,
                HeightBox = heightBox
            });
        }
    }

    private async Task CalibrateTrackerAsync(int trackerId)
    {
        if (!Handler.IsInitialized || CalibrationPending) return;
        if (trackerId < 0 || trackerId >= Handler.TrackerCount) return;

        var trackerRow = _trackerUIRows.FirstOrDefault(r => r.TrackerId == trackerId);
        if (trackerRow == null) return;

        // Block all calibrate buttons
        CalibrationPending = true;
        foreach (var row in _trackerUIRows)
            row.CalibrateButton.IsEnabled = false;

        // Setup calibration UI
        CalibrationTextBlock.Visibility = Visibility.Visible;
        CalibrationTextBlock.Text = $"Calibrating owoTrack {trackerId}: Face forward in 7 seconds...";
        Host.PlayAppSound(SoundType.CalibrationStart);

        // Wait for user to get ready
        await Task.Delay(7000);
        if (!Handler.IsInitialized)
        {
            Handler.SetCalibratingForward(trackerId, false);
            CalibrationTextBlock.Visibility = Visibility.Collapsed;
            Host.PlayAppSound(SoundType.CalibrationAborted);
            CalibrationPending = false;
            foreach (var row in _trackerUIRows)
                row.CalibrateButton.IsEnabled = true;
            return;
        }

        // Forward calibration
        Handler.SetCalibratingForward(trackerId, true);
        CalibrationTextBlock.Text = $"Calibrating owoTrack {trackerId}: Hold still...";
        Host.PlayAppSound(SoundType.CalibrationPointCaptured);

        await Task.Delay(4000);
        Handler.SetCalibratingForward(trackerId, false);

        // Down calibration
        CalibrationTextBlock.Text = $"Calibrating owoTrack {trackerId}: Now look down...";
        Host.PlayAppSound(SoundType.CalibrationPointCaptured);

        await Task.Delay(3000);
        Handler.SetCalibratingDown(trackerId, true);
        CalibrationTextBlock.Text = $"Calibrating owoTrack {trackerId}: Hold still...";

        await Task.Delay(3000);
        Handler.SetCalibratingDown(trackerId, false);
        
        Host.PlayAppSound(SoundType.CalibrationComplete);
        CalibrationTextBlock.Visibility = Visibility.Collapsed;

        // Save calibration
        var settings = GetOrCreateTrackerSettings(trackerId);
        settings.GlobalRotation = Handler.GetGlobalRotation(trackerId).ToNet();
        settings.LocalRotation = Handler.GetLocalRotation(trackerId).ToNet();
        SaveTrackerSettings(trackerId);

        // Unblock buttons
        CalibrationPending = false;
        foreach (var row in _trackerUIRows)
            row.CalibrateButton.IsEnabled = true;

        UpdateSettingsInterface();
    }

    public void Initialize()
    {
        switch (Handler.Initialize())
        {
            case (int)HandlerStatus.ServiceNotStarted:
                Host.Log(
                    $"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ServiceNotStarted}",
                    LogSeverity.Warning);
                break;
            case (int)HandlerStatus.ServiceSuccess:
                Host.Log(
                    $"Successfully initialized the owoTrackVR device handler! Status: {HandlerStatus.ServiceSuccess}");
                break;
            case (int)HandlerStatus.ConnectionDead:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ConnectionDead}",
                    LogSeverity.Warning);
                break;
            case (int)HandlerStatus.ErrorNoData:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ErrorNoData}",
                    LogSeverity.Warning);
                break;
            case (int)HandlerStatus.ErrorInitFailed:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ErrorInitFailed}",
                    LogSeverity.Error);
                break;
            case (int)HandlerStatus.ErrorPortsTaken:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ErrorPortsTaken}",
                    LogSeverity.Fatal);
                break;
        }

        // Refresh the settings interface
        UpdateSettingsInterface(true);
    }

    public void Shutdown()
    {
        switch (Handler.Shutdown())
        {
            case 0:
                Host.Log($"Tried to shutdown the owoTrackVR device handler with status: {Handler.StatusResult}");
                break;
            default:
                Host.Log("Tried to shutdown the owoTrackVR device handler, exception occurred!", LogSeverity.Error);
                break;
        }
    }

    public void Update()
    {
        // That's all if the server is failing!
        if (!PluginLoaded || !Handler.IsInitialized ||
            Handler.StatusResult != (int)HandlerStatus.ServiceSuccess) return;

        // Update poses for all enabled trackers (mapped to joints)
        for (int jointIndex = 0; jointIndex < TrackedJoints.Count; jointIndex++)
        {
            if (!_jointToTrackerMap.TryGetValue(jointIndex, out var trackerId)) continue;
            
            var trackerOffset = GetTrackerOffset(trackerId);
            
            // Get the computed pose for this tracker
            var pose = Handler.CalculatePoseForTracker(
                trackerId,
                new Pose
                {
                    Position = Host.HmdPose.Position.ToWin(),
                    Orientation = Host.HmdPose.Orientation.ToWin()
                },
                (float)Host.HmdOrientationYaw,
                GlobalOffset.ToWin(),
                DeviceOffset.ToWin(),
                trackerOffset.ToWin()
            );

            // Update this joint's pose
            TrackedJoints[jointIndex].Position = pose.Position.ToNet();
            TrackedJoints[jointIndex].Orientation = pose.Orientation.ToNet();
        }
    }

    public void SignalJoint(int jointId)
    {
        // Send a buzz signal to the corresponding tracker
        if (_jointToTrackerMap.TryGetValue(jointId, out var trackerId))
        {
            Handler.SignalTracker(trackerId);
        }
    }

    private void UpdateSettingsInterface(bool forceRefresh = false)
    {
        // That's all if the server is failing!
        if (!PluginLoaded) return;

        // Check if we've got anything to do here
        if (!forceRefresh && _statusBackup == Handler.StatusResult) return;

        // Rebuild tracker UI if needed
        if (_lastTrackerCount != Handler.TrackerCount || forceRefresh)
        {
            RebuildTrackerSettingsUI();
        }

        // Update the settings UI
        if (Handler.StatusResult == (int)HandlerStatus.ServiceSuccess)
        {
            MessageTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            MessageTextBlock.Visibility = Visibility.Visible;
            MessageTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/NotConnected");
        }

        // Cache the status
        _statusBackup = Handler.StatusResult;
    }

    private void StatusChangedEventHandler(object sender, string message)
    {
        // Log what happened
        Host?.Log($"Status interface requested by {sender} with message {message}");

        // Request an interface refresh
        Host?.RefreshStatusInterface();
    }

    private void LogMessageEventHandler(object sender, string message)
    {
        // Compute severity
        var severity = message.Length >= 2
            ? int.TryParse(message[1].ToString(), out var parsed) ? Math.Clamp(parsed, 0, 3) : 0
            : 0; // Default to LogSeverity.Info

        // Log a message to AME
        Host?.Log(message, (LogSeverity)severity);
    }

    private enum HandlerStatus
    {
        ServiceNotStarted = 0x00010005, // Not initialized
        ServiceSuccess = 0, // Success, everything's fine!
        ConnectionDead = 0x00010001, // No connection
        ErrorNoData = 0x00010002, // No data received
        ErrorInitFailed = 0x00010003, // Init failed
        ErrorPortsTaken = 0x00010004 // Ports taken
    }

    #region UI Elements

    private TextBlock IpTextBlock { get; set; }
    private TextBlock IpLabelTextBlock { get; set; }
    private TextBlock PortTextBlock { get; set; }
    private TextBlock PortLabelTextBlock { get; set; }
    private TextBlock MessageTextBlock { get; set; }
    private TextBlock CalibrationTextBlock { get; set; }

    #endregion
}

internal static class ProjectionExtensions
{
    public static DVector ToWin(this Vector3 vector)
    {
        return new DVector(vector.X, vector.Y, vector.Z);
    }

    public static DQuaternion ToWin(this Quaternion quaternion)
    {
        return new DQuaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }

    public static Vector3 ToNet(this DVector vector)
    {
        return new Vector3(vector.X, vector.Y, vector.Z);
    }

    public static Quaternion ToNet(this DQuaternion quaternion)
    {
        return new Quaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }
}

internal class SetupData : ICoreSetupData
{
    public object PluginIcon => new PathIcon
    {
        Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry),
            "M34.86,0A34.76,34.76,0,1,0,69.51,34.9,34.74,34.74,0,0,0,34.86,0ZM14.33,18.66A25.12,25.12,0,0,1,29.22,9.31c1.81-.39,3.68-.53,5-.7a28.81,28.81,0,0,1,11,2.27,1.45,1.45,0,0,1,1,1.45q.3,4.06.75,8.11c.09.79-.21.82-.79.69-.36-.08-.72-.16-1.08-.26-2-.59-1.88-.22-1.82-2.46,0-1.38-.42-1.79-1.73-1.64s-2.87.17-4.30.34c-.28,0-.59.43-.76.72a1.43,1.43,0,0,0,0,.8c.12,1.07-.07,1.62-1.41,1.84a31.91,31.91,0,0,0-16.08,7.77c-.56.48-.86.48-1.18-.2-1.22-2.62-2.50-5.21-3.67-7.85A1.86,1.86,0,0,1,14.33,18.66ZM24.91,58.73l-.09-.14h-.05v0c-2.26-.16-3.92-1.62-5.56-2.89A25.59,25.59,0,0,1,8.92,38.08a27.88,27.88,0,0,1,.87-10.71c.39.68.71,1.18,1,1.71q7,14.51,14,29a1.27,1.27,0,0,1,.05.44h0l0,0,.09,0S24.91,58.70,24.91,58.73Zm21.17-.43a29.5,29.5,0,0,1-8.72,2.43c-.18,0-.66-.57-.65-.86,0-1.86.16-3.72.28-5.58s.28-3.82.43-5.73c.10-1.25.22-2.50.34-4a4.44,4.44,0,0,1,.75.64c2.65,3.87,5.27,7.76,8,11.60C47,57.61,47,57.89,46.08,58.30Zm2.79-9.88C45.58,43.62,42.25,38.85,39,34c-.8-1.2-1.55-1.74-3-1.06a13.43,13.43,0,0,1-2.29.67c-2.05.56-2.14.68-2.27,2.76Q31,44.77,30.45,53.13a5.59,5.59,0,0,1-.29.83l-4.82-10c-1.32-2.75-2.58-5.52-4-8.23a1.65,1.65,0,0,1,.45-2.32,27.53,27.53,0,0,1,14-7.25c.83-.16,1.23.05,1.14.95,0,.32,0,.64,0,1,0,1.08.12,1.69,1.54,1.34a18.06,18.06,0,0,1,4.29-.32c.94,0,1.06-.46,1-1.2-.18-1.24.42-1.31,1.42-1,2.29.66,2.45.86,2.67,4.06.25,3.62.48,7.23.77,10.84.18,2.20.46,4.40.69,6.59Zm7.59.79l-.4-.09Q54.69,33,53.31,16.94c2.24,1,5.91,7.40,6.84,11.78A26.07,26.07,0,0,1,56.46,49.21Z")
    };

    public string GroupName => string.Empty;
    public Type PluginType => typeof(ITrackingDevice);
}