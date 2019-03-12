﻿using Remotely_Agent.Client;
using Remotely_Library.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management.Automation;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;

namespace Remotely_Agent.Services
{
    public class Updater
    {
        internal static async Task<string> GetLatestScreenCastVersion()
        {
            var platform = "";
            if (OSUtils.IsWindows)
            {
                platform = "Windows";
            }
            else if (OSUtils.IsLinux)
            {
                platform = "Linux";
            }
            else
            {
                throw new Exception("Unsupported operating system.");
            }
            var response = await new HttpClient().GetAsync(Utilities.GetConnectionInfo().Host + $"/API/ScreenCastVersion/{platform}");
            return await response.Content.ReadAsStringAsync();
        }


        internal static void CheckForCoreUpdates()
        {
            try
            {
                var wc = new WebClient();
                var latestVersion = wc.DownloadString(Utilities.GetConnectionInfo().Host + $"/Downloads/CurrentAgentVersion.txt").Trim();
                var thisVersion = FileVersionInfo.GetVersionInfo("Remotely_Agent.dll").FileVersion.ToString().Trim();
                if (thisVersion != latestVersion)
                {
                    Logger.Write($"Service Updater: Downloading update.  Current Version: {thisVersion}.  Latest Version: {latestVersion}.");
                    var fileName = OSUtils.CoreZipFileName;
                    var tempFile = Path.Combine(Path.GetTempPath(), fileName);
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                    wc.DownloadFile(new Uri(Utilities.GetConnectionInfo().Host + $"/Downloads/{fileName}"), tempFile);

                    Logger.Write($"Service Updater: Extracting files.");

                    if (OSUtils.IsWindows)
                    {
                        ZipFile.ExtractToDirectory(tempFile, Path.Combine(Path.GetTempPath(), "Remotely_Update"), true);
                    }
                    else if (OSUtils.IsLinux)
                    {
                        Process.Start("sudo", $"rm -r {Path.Combine(Path.GetTempPath(), "Remotely_Update")}");
                        Process.Start("sudo", "apt-get install unzip").WaitForExit();
                        Process.Start("sudo", $"unzip -o {tempFile} -d {Path.Combine(Path.GetTempPath(), "Remotely_Update")}").WaitForExit();
                        Process.Start("sudo", $"chmod -R 777 {Path.Combine(Path.GetTempPath(), "Remotely_Update")}").WaitForExit();
                        Process.Start("sudo", $"chmod +x {Path.Combine(Path.GetTempPath(), "Remotely_Update", "Remotely_Agent")}").WaitForExit();
                    }

                    Logger.Write($"Service Updater: Launching extracted process to perform update.");
                    if (OSUtils.IsWindows)
                    {
                        var psi = new ProcessStartInfo()
                        {
                            FileName = Path.Combine(Path.GetTempPath(), "Remotely_Update", OSUtils.ClientExecutableFileName),
                            Arguments = "-update true",
                            Verb = "RunAs"
                        };
                        Process.Start(psi);
                    }
                    else if (OSUtils.IsLinux)
                    {
                        Process.Start("sudo", $"{Path.Combine(Path.GetTempPath(), "Remotely_Update", "Remotely_Agent")} -update true & disown");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }
        internal static void CoreUpdate()
        {
            try
            {
                Logger.Write("Service Updater: Starting update.");
                var ps = PowerShell.Create();
                if (OSUtils.IsWindows)
                {
                    ps.AddScript(@"Get-Service | Where-Object {$_.Name -like ""Remotely_Service""} | Stop-Service -Force");
                    ps.Invoke();
                    ps.Commands.Clear();
                }
                else if (OSUtils.IsLinux)
                {
                    Process.Start("sudo", "systemctl stop remotely-agent");
                }


                foreach (var proc in Process.GetProcesses().Where(x =>
                                                x.ProcessName.Contains("Remotely_Agent") &&
                                                x.Id != Process.GetCurrentProcess().Id))
                {
                    proc.Kill();
                }

                string targetDir = "";

                if (OSUtils.IsWindows)
                {
                    targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remotely");
                }
                else if (OSUtils.IsLinux)
                {
                    targetDir = "/usr/local/bin/Remotely";
                }
 
                Logger.Write("Service Updater: Copying new files.");


                var fileList = Directory.GetFiles(Path.Combine(Path.GetTempPath(), "Remotely_Update"));
                foreach (var file in fileList)
                {
                    try
                    {
                        var targetPath = Path.Combine(targetDir, Path.GetFileName(file));
                        File.Copy(file, targetPath, true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(ex);
                        continue;
                    }
                }


                var subdirList = Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "Remotely_Update"));

                foreach (var subdir in subdirList)
                {
                    try
                    {
                        var targetPath = Path.Combine(targetDir, Path.GetFileName(subdir.TrimEnd(Path.DirectorySeparatorChar)));
                        if (Directory.Exists(targetPath))
                        {
                            Directory.Delete(targetPath, true);
                        }
                        Directory.Move(subdir, targetPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(ex);
                        continue;
                    }
                    
                }
                Logger.Write("Service Updater: Update completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
            finally
            {
                Logger.Write("Service Updater: Starting service.");
                if (OSUtils.IsWindows)
                {
                    var ps = PowerShell.Create();
                    ps.AddScript("Start-Service -Name \"Remotely_Service\"");
                    ps.Invoke();
                }
                else if (OSUtils.IsLinux)
                {
                    Process.Start("sudo", "systemctl restart remotely-agent").WaitForExit();
                }
                Environment.Exit(0);
            }
        }
    }
}
