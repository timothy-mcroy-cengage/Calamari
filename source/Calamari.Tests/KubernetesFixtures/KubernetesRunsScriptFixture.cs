﻿#if KUBERNETES
using System;
using System.Collections.Specialized;
using System.IO;
using Alphaleonis.Win32.Filesystem;
using Calamari.Hooks;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Processes;
using Calamari.Integration.Scripting;
using Calamari.Integration.Scripting.WindowsPowerShell;
using Calamari.Kubernetes;
using Calamari.Tests.Helpers;
using NUnit.Framework;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Calamari.Tests.KubernetesFixtures
{
    [TestFixture]
    [Ignore("Not Yet")]
    public class KubernetesContextScriptWrapperFixture
    {
        const string ClusterTokenEnvironmentVariable = "OCTOPUS_K8S_TOKEN";
        const string CluserServerEnvironmentVariable = "OCTOPUS_K8S_SERVER";

        //See "GitHub Test Account"
        private static readonly string ClusterUri = Environment.GetEnvironmentVariable("K8S_OctopusAPITester_Server");
        static readonly string ClusterToken = Environment.GetEnvironmentVariable("K8S_OctopusAPITester_Token");
        
        [Test]
        [Category(TestEnvironment.CompatibleOS.Windows)]
        public void PowershellKubeCtlScripts()
        {
            var wrapper = new KubernetesContextScriptWrapper(new CalamariVariableDictionary());
            TestScript(wrapper, "Test-Script.ps1");
        }
        
        [Test]

        [Category(TestEnvironment.CompatibleOS.Nix)]
        public void BashKubeCtlScripts()
        {
            //TestScript(new KubernetesBashScriptEngine(), "Test-Script.sh");
        }

        private void TestScript(IScriptWrapper wrapper, string scriptName)
        {
            using (var dir = TemporaryDirectory.Create())
            using (var temp = new TemporaryFile(Path.Combine(dir.DirectoryPath, scriptName)))
            {
                File.WriteAllText(temp.FilePath, "kubectl get nodes");
                var output = ExecuteScript(wrapper, temp.FilePath, new CalamariVariableDictionary()
                {
                    ["OctopusKubernetesServer"] = ClusterUri,
                    ["OctopusKubernetesToken"] = ClusterToken,
                    ["OctopusKubernetesInsecure"] = "true"

                });
                output.AssertSuccess();
                output.AssertOutput("ASKROB");
            }
        }

        private CalamariResult ExecuteScript(IScriptWrapper wrapper, string scriptName, CalamariVariableDictionary variables)
        {
            var capture = new CaptureCommandOutput();
            var runner = new CommandLineRunner(capture);
            wrapper.NextWrapper = new TerminalScriptWrapper(new PowerShellScriptEngine());
            var result = wrapper.ExecuteScript(new Script(scriptName), variables, runner, new StringDictionary());
            //var result = psse.Execute(new Script(scriptName), variables, runner);
            return new CalamariResult(result.ExitCode, capture);
        }
    }
}
#endif