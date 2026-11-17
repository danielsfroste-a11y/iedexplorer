/*
 *  Copyright (C) 2013 Pavel Charvat
 * 
 *  This file is part of IEDExplorer.
 *
 *  IEDExplorer is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  IEDExplorer is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with IEDExplorer.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using PcapDotNet.Core;

namespace IEDExplorer
{
    class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--scada-selftest") >= 0)
            {
                try
                {
                    ScadaBreakerSelfTest.Run();
                    File.WriteAllText("scada-selftest-result.txt", "PASS" + Environment.NewLine);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    File.WriteAllText("scada-selftest-result.txt", "FAIL: " + ex);
                    Environment.Exit(1);
                }
                return;
            }
            foreach (string arg in args)
            {
                if (arg.StartsWith("--tcp102-probe=", StringComparison.OrdinalIgnoreCase))
                {
                    string host = arg.Substring("--tcp102-probe=".Length);
                    try
                    {
                        // Transport-only smoke check. It sends no MMS requests or controls.
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            IAsyncResult ar = client.BeginConnect(host, 102, null, null);
                            if (!ar.AsyncWaitHandle.WaitOne(4000))
                                throw new TimeoutException("TCP/102 connect timeout");
                            client.EndConnect(ar);
                            File.WriteAllText("tcp102-probe-result.txt",
                                "TCP 102 CONNECTED: " + host +
                                "; not an MMS or IEC 61850 data read verification.");
                        }
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText("tcp102-probe-result.txt",
                            "TCP 102 NOT REACHABLE: " + host + ": " + ex.Message);
                        Environment.Exit(2);
                    }
                    return;
                }
            }
            string resPrefix = "IEDExplorer.Embed.";
            string filename1 = "iec61850dotnet.dll";
            string filename2 = "iec61850.dll";
            string filename3 = "PcapDotNet.Base.dll";
            string filename4 = "PcapDotNet.Core.dll";
            string filename5 = "PcapDotNet.Core.Extensions.dll";
            string filename6 = "PcapDotNet.Packets.dll";

            EmbeddedAssembly.Load(resPrefix + filename1, filename1);
            EmbeddedAssembly.Load(resPrefix + filename2, filename2);
            EmbeddedAssembly.Load(resPrefix + filename3, filename3);
            EmbeddedAssembly.Load(resPrefix + filename4, filename4);
            EmbeddedAssembly.Load(resPrefix + filename5, filename5);
            EmbeddedAssembly.Load(resPrefix + filename6, filename6);

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Env env = new Env();
            string readOnlyMmsHost = null;
            foreach (string arg in args)
                if (arg.StartsWith("--mms-readonly-probe=", StringComparison.OrdinalIgnoreCase))
                    readOnlyMmsHost = arg.Substring("--mms-readonly-probe=".Length);
            if (readOnlyMmsHost != null)
            {
                try { Environment.Exit(ScadaMmsReadOnlyProbe.Run(readOnlyMmsHost)); }
                catch (Exception ex)
                {
                    File.WriteAllText("mms-probe-result.txt", "MMS PROBE ERROR: " + ex);
                    Environment.Exit(3);
                }
                return;
            }
            Application.Run(new Views.MainWindow());

            List<string> natives = new List<string>();
            natives.Add(filename2);
            //natives.Add(filename4);

            foreach (string filename in natives)
            {
                string path2 = null;
                try
                {
                    path2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
                    if (File.Exists(path2))
                        File.Delete(path2);
                }
                catch
                {
                    try
                    {
                        //AppDomain.CurrentDomain.DomainManager. //.Unload(AppDomain.CurrentDomain);
                        UnloadImportedDll(path2);
                        Thread.Sleep(100);
                        File.Delete(path2);
                    }
                    catch { }
                }
            }
        }

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            return EmbeddedAssembly.Get(args.Name);
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public static void UnloadImportedDll(string DllPath)
        {
            foreach (System.Diagnostics.ProcessModule mod in System.Diagnostics.Process.GetCurrentProcess().Modules)
            {
                if (mod.FileName.ToUpper() == DllPath.ToUpper())
                {
                    FreeLibrary(mod.BaseAddress);
                }
            }
        }
    }
}
