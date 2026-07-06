using Microsoft.Win32;
using SharpPcap.LibPcap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace SpsLogic
{
    /// <summary>
    /// Manual probes for WinPcap installation and bundled installer launch behavior.
    /// Npcap evidence is logged only because it may provide WinPcap-compatible runtime files.
    /// </summary>
    public static class WinPcapInstallTest
    {
        public const string InstallerDirectoryName = "winpcap";
        public const string InstallerFileName = "WinPcap_4_1_3.exe";

        public const string WinPcapLicenseUrl = "https://www.winpcap.org/misc/copyright.htm";
        public const string WinPcapDownloadUrl = "https://www.winpcap.org/install/";
        public const string NpcapUrl = "https://npcap.com/";

        private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";

        public sealed class DetectionReport
        {
            internal DetectionReport(
                bool winPcapEvidenceFound,
                bool winPcapCompatibleApiFilesFound,
                bool npcCapEvidenceFound,
                int? captureDeviceCount,
                string captureProbeError,
                string[] evidence,
                string[] missingEvidence)
            {
                WinPcapEvidenceFound = winPcapEvidenceFound;
                WinPcapCompatibleApiFilesFound = winPcapCompatibleApiFilesFound;
                NpcapEvidenceFound = npcCapEvidenceFound;
                CaptureDeviceCount = captureDeviceCount;
                CaptureProbeError = captureProbeError;
                Evidence = evidence ?? new string[0];
                MissingEvidence = missingEvidence ?? new string[0];
            }

            public bool WinPcapEvidenceFound { get; private set; }
            public bool WinPcapCompatibleApiFilesFound { get; private set; }
            public bool NpcapEvidenceFound { get; private set; }
            public int? CaptureDeviceCount { get; private set; }
            public string CaptureProbeError { get; private set; }
            public string[] Evidence { get; private set; }
            public string[] MissingEvidence { get; private set; }

            public bool CaptureRuntimeUsable
            {
                get { return CaptureDeviceCount.HasValue && CaptureDeviceCount.Value > 0; }
            }

            public bool ShouldOfferWinPcapInstaller
            {
                get { return !WinPcapEvidenceFound && !CaptureRuntimeUsable; }
            }

            public bool AnyPcapEvidenceFound
            {
                get { return WinPcapEvidenceFound || WinPcapCompatibleApiFilesFound || NpcapEvidenceFound || CaptureRuntimeUsable; }
            }

            public string ToLogText()
            {
                return
                    "WinPcap detection:" + Environment.NewLine +
                    "  WinPcapEvidenceFound=" + WinPcapEvidenceFound + Environment.NewLine +
                    "  WinPcapCompatibleApiFilesFound=" + WinPcapCompatibleApiFilesFound + Environment.NewLine +
                    "  NpcapEvidenceFound=" + NpcapEvidenceFound + Environment.NewLine +
                    "  CaptureDeviceCount=" + (CaptureDeviceCount.HasValue ? CaptureDeviceCount.Value.ToString() : "unknown") + Environment.NewLine +
                    "  CaptureRuntimeUsable=" + CaptureRuntimeUsable + Environment.NewLine +
                    "  ShouldOfferWinPcapInstaller=" + ShouldOfferWinPcapInstaller + Environment.NewLine +
                    "  CaptureProbeError=" + (string.IsNullOrEmpty(CaptureProbeError) ? "none" : CaptureProbeError) + Environment.NewLine +
                    "  Evidence=" + FormatList(Evidence) + Environment.NewLine +
                    "  MissingEvidence=" + FormatList(MissingEvidence);
            }

            public override string ToString()
            {
                return ToLogText();
            }
        }

        public sealed class InstallerLaunchReport
        {
            internal InstallerLaunchReport(
                string installerPath,
                bool installerExists,
                bool launchRequested,
                bool launched,
                int? processId,
                string error)
            {
                InstallerPath = installerPath;
                InstallerExists = installerExists;
                LaunchRequested = launchRequested;
                Launched = launched;
                ProcessId = processId;
                Error = error;
            }

            public string InstallerPath { get; private set; }
            public bool InstallerExists { get; private set; }
            public bool LaunchRequested { get; private set; }
            public bool Launched { get; private set; }
            public int? ProcessId { get; private set; }
            public string Error { get; private set; }

            public string ToLogText()
            {
                return
                    "WinPcap installer launch:" + Environment.NewLine +
                    "  InstallerPath=" + InstallerPath + Environment.NewLine +
                    "  InstallerExists=" + InstallerExists + Environment.NewLine +
                    "  LaunchRequested=" + LaunchRequested + Environment.NewLine +
                    "  Launched=" + Launched + Environment.NewLine +
                    "  ProcessId=" + (ProcessId.HasValue ? ProcessId.Value.ToString() : "none") + Environment.NewLine +
                    "  Error=" + (string.IsNullOrEmpty(Error) ? "none" : Error);
            }

            public override string ToString()
            {
                return ToLogText();
            }
        }

        public static string GetBundledInstallerPath(string baseDirectory = null)
        {
            string root = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;

            return Path.GetFullPath(Path.Combine(root, InstallerDirectoryName, InstallerFileName));
        }

        public static bool IsBundledInstallerAvailable(string baseDirectory = null)
        {
            return File.Exists(GetBundledInstallerPath(baseDirectory));
        }

        public static DetectionReport Detect()
        {
            var evidence = new List<string>();
            var missingEvidence = new List<string>();

            bool winPcapEvidenceFound = false;
            bool winPcapCompatibleApiFilesFound = false;
            bool npcCapEvidenceFound = false;

            winPcapEvidenceFound |= ProbeKnownFile(@"System32\drivers\npf.sys", "WinPcap NPF driver", evidence, missingEvidence);
            winPcapEvidenceFound |= ProbeServiceKey("NPF", "WinPcap NPF service", evidence, missingEvidence);
            winPcapEvidenceFound |= ProbeUninstallEntry("WinPcap", evidence);

            winPcapCompatibleApiFilesFound |= ProbeKnownFile(@"System32\wpcap.dll", "WinPcap-compatible DLL", evidence, missingEvidence);
            winPcapCompatibleApiFilesFound |= ProbeKnownFile(@"System32\Packet.dll", "WinPcap-compatible packet DLL", evidence, missingEvidence);
            winPcapCompatibleApiFilesFound |= ProbeKnownFile(@"SysWOW64\wpcap.dll", "WinPcap-compatible 32-bit DLL", evidence, missingEvidence);
            winPcapCompatibleApiFilesFound |= ProbeKnownFile(@"SysWOW64\Packet.dll", "WinPcap-compatible 32-bit packet DLL", evidence, missingEvidence);

            npcCapEvidenceFound |= ProbeKnownFile(@"System32\Npcap\wpcap.dll", "Npcap DLL", evidence, missingEvidence);
            npcCapEvidenceFound |= ProbeKnownFile(@"System32\Npcap\Packet.dll", "Npcap packet DLL", evidence, missingEvidence);
            npcCapEvidenceFound |= ProbeKnownFile(@"System32\drivers\npcap.sys", "Npcap driver", evidence, missingEvidence);
            npcCapEvidenceFound |= ProbeServiceKey("npcap", "Npcap service", evidence, missingEvidence);
            npcCapEvidenceFound |= ProbeServiceKey("npcap_wifi", "Npcap Wi-Fi service", evidence, missingEvidence);
            npcCapEvidenceFound |= ProbeUninstallEntry("Npcap", evidence);

            int? captureDeviceCount;
            string captureProbeError;
            ProbeSharpPcapDeviceList(out captureDeviceCount, out captureProbeError, evidence);

            return new DetectionReport(
                winPcapEvidenceFound,
                winPcapCompatibleApiFilesFound,
                npcCapEvidenceFound,
                captureDeviceCount,
                captureProbeError,
                evidence.ToArray(),
                missingEvidence.ToArray());
        }

        public static void TestDetection()
        {
            Logger.Log(Detect().ToLogText(), true);
        }

        public static InstallerLaunchReport TestInstallerLaunch(
            bool startInstaller = false,
            string baseDirectory = null,
            bool runAsAdministrator = true)
        {
            string installerPath = GetBundledInstallerPath(baseDirectory);
            bool installerExists = File.Exists(installerPath);

            if (!installerExists)
            {
                return new InstallerLaunchReport(
                    installerPath,
                    false,
                    startInstaller,
                    false,
                    null,
                    "Installer file was not found.");
            }

            if (!startInstaller)
            {
                return new InstallerLaunchReport(
                    installerPath,
                    true,
                    false,
                    false,
                    null,
                    "Dry run only. Pass startInstaller: true to launch.");
            }

            try
            {
                var startInfo = new ProcessStartInfo(installerPath)
                {
                    UseShellExecute = true
                };

                if (runAsAdministrator)
                {
                    startInfo.Verb = "runas";
                }

                Process process = Process.Start(startInfo);
                return new InstallerLaunchReport(
                    installerPath,
                    true,
                    true,
                    process != null,
                    process == null ? (int?)null : process.Id,
                    null);
            }
            catch (Exception ex)
            {
                return new InstallerLaunchReport(
                    installerPath,
                    true,
                    true,
                    false,
                    null,
                    FormatException(ex));
            }
        }

        public static void TestInstallerLaunchDryRun(string baseDirectory = null)
        {
            Logger.Log(TestInstallerLaunch(false, baseDirectory).ToLogText(), true);
        }

        public static void TestInstallerLaunchActual(string baseDirectory = null)
        {
            Logger.Log(TestInstallerLaunch(true, baseDirectory).ToLogText(), true);
        }

        public static void LogLicenseCheckNotes()
        {
            Logger.Log(
                "WinPcap license check notes:" + Environment.NewLine +
                "  WinPcap binary redistribution is permitted if copyright, conditions, and disclaimer are reproduced in documentation/materials." + Environment.NewLine +
                "  Do not use Politecnico di Torino, CACE Technologies, or contributor names to endorse SPS without written permission." + Environment.NewLine +
                "  Official download page says WinPcap is discontinued and unsupported." + Environment.NewLine +
                "  Npcap redistribution is intentionally not used here because Npcap Free Edition is not externally redistributable." + Environment.NewLine +
                "  Sources:" + Environment.NewLine +
                "    " + WinPcapLicenseUrl + Environment.NewLine +
                "    " + WinPcapDownloadUrl + Environment.NewLine +
                "    " + NpcapUrl,
                true);
        }

        public static void RunAll(bool startInstaller = false, string baseDirectory = null)
        {
            TestDetection();
            LogLicenseCheckNotes();
            Logger.Log(TestInstallerLaunch(startInstaller, baseDirectory).ToLogText(), true);
        }

        private static bool ProbeKnownFile(
            string windowsRelativePath,
            string label,
            List<string> evidence,
            List<string> missingEvidence)
        {
            string path = Path.Combine(GetWindowsDirectory(), windowsRelativePath);
            if (File.Exists(path))
            {
                evidence.Add(label + ": " + path);
                return true;
            }

            missingEvidence.Add(label + ": " + path);
            return false;
        }

        private static bool ProbeServiceKey(
            string serviceName,
            string label,
            List<string> evidence,
            List<string> missingEvidence)
        {
            string keyPath = ServicesRegistryPath + "\\" + serviceName;

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        evidence.Add(label + ": HKLM\\" + keyPath);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                missingEvidence.Add(label + " registry probe failed: " + FormatException(ex));
                return false;
            }

            missingEvidence.Add(label + ": HKLM\\" + keyPath);
            return false;
        }

        private static bool ProbeUninstallEntry(string productName, List<string> evidence)
        {
            bool found = false;
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in GetRegistryViews())
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey uninstallKey = baseKey.OpenSubKey(UninstallRegistryPath))
                        {
                            if (uninstallKey == null)
                            {
                                continue;
                            }

                            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                            {
                                using (RegistryKey appKey = uninstallKey.OpenSubKey(subKeyName))
                                {
                                    string displayName = appKey == null
                                        ? null
                                        : appKey.GetValue("DisplayName") as string;

                                    if (displayName != null &&
                                        displayName.IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        evidence.Add(
                                            productName + " uninstall entry: " +
                                            hive + "\\" + view + "\\" + subKeyName + " (" + displayName + ")");
                                        found = true;
                                    }
                                }
                            }
                        }
                    }
                    catch (SecurityException ex)
                    {
                        evidence.Add(productName + " uninstall registry probe skipped: " + FormatException(ex));
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        evidence.Add(productName + " uninstall registry probe skipped: " + FormatException(ex));
                    }
                    catch (IOException ex)
                    {
                        evidence.Add(productName + " uninstall registry probe failed: " + FormatException(ex));
                    }
                }
            }

            return found;
        }

        private static void ProbeSharpPcapDeviceList(
            out int? captureDeviceCount,
            out string captureProbeError,
            List<string> evidence)
        {
            captureDeviceCount = null;
            captureProbeError = null;

            try
            {
                var devices = LibPcapLiveDeviceList.Instance;
                captureDeviceCount = devices.Count;
                evidence.Add("SharpPcap LibPcapLiveDeviceList.Instance count: " + devices.Count);

                for (int i = 0; i < Math.Min(devices.Count, 5); i++)
                {
                    evidence.Add(
                        "SharpPcap device[" + i + "]: " +
                        devices[i].Name + " - " + devices[i].Description);
                }
            }
            catch (Exception ex)
            {
                captureProbeError = FormatException(ex);
                evidence.Add("SharpPcap device list probe failed: " + captureProbeError);
            }
        }

        private static RegistryView[] GetRegistryViews()
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return new[] { RegistryView.Registry64, RegistryView.Registry32 };
            }

            return new[] { RegistryView.Registry32 };
        }

        private static string GetWindowsDirectory()
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return string.IsNullOrWhiteSpace(windowsDirectory)
                ? Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows"
                : windowsDirectory;
        }

        private static string FormatList(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "none";
            }

            return Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", values);
        }

        private static string FormatException(Exception ex)
        {
            if (ex == null)
            {
                return "none";
            }

            Exception baseException = ex.GetBaseException();
            return baseException.GetType().Name + ": " + baseException.Message;
        }
    }
}
