﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Calamari.Common.Features.Processes;
using Calamari.Common.Plumbing;
using Calamari.Common.Plumbing.FileSystem;
using Calamari.Common.Plumbing.Retry;
using Calamari.Testing.Helpers;
using Newtonsoft.Json.Linq;

namespace Calamari.Tests.KubernetesFixtures
{
    class InstallTools
    {
        public string TerraformExecutable { get; private set; }
        public string KubectlExecutable { get; private set; }
        public string AwsAuthenticatorExecutable { get; private set; }

        public async Task Install()
        {
            using (var client = new HttpClient())
            {
                TerraformExecutable = await DownloadCli("Terraform",
                                                              async () =>
                                                              {
                                                                  var json = await client.GetAsync("https://checkpoint-api.hashicorp.com/v1/check/terraform");
                                                                  json.EnsureSuccessStatusCode();
                                                                  var jObject = JObject.Parse(await json.Content.ReadAsStringAsync());
                                                                  var downloadBaseUrl = jObject["current_download_url"].Value<string>();
                                                                  var version = jObject["current_version"].Value<string>();
                                                                  return (version, downloadBaseUrl);
                                                              },
                                                              async (destinationDirectoryName, tuple) =>
                                                              {
                                                                  var fileName = GetTerraformFileName(tuple.latestVersion);

                                                                  await DownloadTerraform(fileName, client, tuple.data, destinationDirectoryName);

                                                                  var terraformExecutable = Directory.EnumerateFiles(destinationDirectoryName).FirstOrDefault();
                                                                  return terraformExecutable;
                                                              });

                KubectlExecutable = await DownloadCli("Kubectl",
                                                            async () =>
                                                            {
                                                                var message = await client.GetAsync("https://storage.googleapis.com/kubernetes-release/release/stable.txt");
                                                                message.EnsureSuccessStatusCode();
                                                                return (await message.Content.ReadAsStringAsync(), null);
                                                            },
                                                            async (destinationDirectoryName, tuple) =>
                                                            {
                                                                var downloadUrl = GetKubectlDownloadLink(tuple.latestVersion);

                                                                await Download(Path.Combine(destinationDirectoryName, GetKubectlFileName()), client, downloadUrl);

                                                                var terraformExecutable = Directory.EnumerateFiles(destinationDirectoryName).FirstOrDefault();
                                                                return terraformExecutable;
                                                            });

                AwsAuthenticatorExecutable = await DownloadCli("aws-iam-authenticator",
                                                                     async () =>
                                                                     {
                                                                         client.DefaultRequestHeaders.Add("User-Agent", "Octopus");
                                                                         var json = await client.GetAsync("https://api.github.com/repos/kubernetes-sigs/aws-iam-authenticator/releases/latest");
                                                                         var r = await json.Content.ReadAsStringAsync();
                                                                         json.EnsureSuccessStatusCode();
                                                                         var jObject = JObject.Parse(await json.Content.ReadAsStringAsync());
                                                                         var downloadUrl = jObject["assets"].Children().FirstOrDefault(token => token["name"].Value<string>().EndsWith(GetAWSAuthenticatorFileNameEndsWith()))?["browser_download_url"].Value<string>();
                                                                         return (jObject["tag_name"].Value<string>(), downloadUrl);
                                                                     },
                                                                     async (destinationDirectoryName, tuple) =>
                                                                     {
                                                                         await Download(Path.Combine(destinationDirectoryName, GetAWSAuthenticatorFileName()), client, tuple.data);

                                                                         var terraformExecutable = Directory.EnumerateFiles(destinationDirectoryName).FirstOrDefault();
                                                                         return terraformExecutable;
                                                                     });
            }
        }

        static void AddExecutePermission(string exePath)
        {
            if (CalamariEnvironment.IsRunningOnWindows)
                return;
            var stdOut = new StringBuilder();
            var stdError = new StringBuilder();
            if (SilentProcessRunner.ExecuteCommand("chmod",
                                                   "+x " + exePath,
                                                   Path.GetDirectoryName(exePath) ?? string.Empty,
                                                   (Action<string>)(s => stdOut.AppendLine(s)),
                                                   (Action<string>)(s => stdError.AppendLine(s)))
                                   .ExitCode
                != 0)
                throw new Exception(stdOut.ToString() + stdError.ToString());
        }

        static string GetTerraformFileName(string currentVersion)
        {
            if (CalamariEnvironment.IsRunningOnNix)
                return $"terraform_{currentVersion}_linux_amd64.zip";
            if (CalamariEnvironment.IsRunningOnMac)
                return $"terraform_{currentVersion}_darwin_amd64.zip";

            return $"terraform_{currentVersion}_windows_amd64.zip";
        }

        static string GetAWSAuthenticatorFileNameEndsWith()
        {
            if (CalamariEnvironment.IsRunningOnNix)
                return "_linux_amd64";
            if (CalamariEnvironment.IsRunningOnMac)
                return "_darwin_amd64";

            return "_windows_amd64.exe";
        }

        static string GetKubectlFileName()
        {
            if (CalamariEnvironment.IsRunningOnWindows)
                return "kubectl.exe";

            return "kubectl";
        }

        static string GetAWSAuthenticatorFileName()
        {
            if (CalamariEnvironment.IsRunningOnWindows)
                return "aws-iam-authenticator.exe";

            return "aws-iam-authenticator";
        }

        static string GetKubectlDownloadLink(string currentVersion)
        {
            if (CalamariEnvironment.IsRunningOnNix)
                return $"https://dl.k8s.io/release/{currentVersion}/bin/linux/amd64/kubectl";
            if (CalamariEnvironment.IsRunningOnMac)
                return $"https://dl.k8s.io/release/{currentVersion}/bin/darwin/amd64/kubectl";

            return $"https://dl.k8s.io/release/{currentVersion}/bin/windows/amd64/kubectl.exe";
        }

        static async Task Download(string path,
                                   HttpClient client,
                                   string downloadUrl)
        {
            using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var stream = await client.GetStreamAsync(downloadUrl))
            {
                await stream.CopyToAsync(fileStream);
            }
        }

        static async Task DownloadTerraform(string fileName,
                                            HttpClient client,
                                            string downloadBaseUrl,
                                            string destination)
        {
            var zipPath = Path.Combine(Path.GetTempPath(), fileName);
            using (new TemporaryFile(zipPath))
            {
                await Download(zipPath, client, $"{downloadBaseUrl}{fileName}");

                ZipFile.ExtractToDirectory(zipPath, destination);
            }
        }

        static async Task<string> DownloadCli(string toolName, Func<Task<(string latestVersion, string data)>> versionFetcher, Func<string, (string latestVersion, string data), Task<string>> downloader)
        {
            var data = await versionFetcher();
            var destinationDirectoryName = TestEnvironment.GetTestPath("Tools", toolName, data.latestVersion);

            string ShouldDownload()
            {
                if (!Directory.Exists(destinationDirectoryName))
                {
                    return null;
                }

                var path = Directory.EnumerateFiles(destinationDirectoryName).FirstOrDefault();
                if (path == null)
                {
                    return null;
                }

                Console.WriteLine($"Using existing {toolName} located in {path}");
                return path;
            }

            var executablePath = ShouldDownload();
            if (!String.IsNullOrEmpty(executablePath))
            {
                return executablePath;
            }

            Console.WriteLine($"Downloading {toolName} cli...");
            Directory.CreateDirectory(destinationDirectoryName);

            var retry = new RetryTracker(3, TimeSpan.MaxValue, new LimitedExponentialRetryInterval(1000, 30000, 2));
            while (retry.Try())
            {
                try
                {
                    executablePath = await downloader(destinationDirectoryName, data);

                    AddExecutePermission(executablePath);
                    break;
                }
                catch
                {
                    if (!retry.CanRetry())
                    {
                        throw;
                    }

                    await Task.Delay(retry.Sleep());
                }
            }

            Console.WriteLine($"Downloaded {toolName} to {executablePath}");

            return executablePath;
        }
    }
}