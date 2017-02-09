﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

using System.Linq;
namespace Blackduck.Hub
{
    static class Scanner
    {
        private static readonly SHA1Cng sha1 = new SHA1Cng();

        private static void EnqueueAll<T>(this Queue<T> queue, IEnumerable<T> elements)
        {
            foreach (T element in elements)
                queue.Enqueue(element);
        }

        static void Main(string[] args)
        {
            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                Console.WriteLine("Argument required. Whaddaya want me to scan?");
                return;
            }


            string target = Path.GetFullPath(args[0]);
            Console.WriteLine("Scanning " + target + "...");

            ScannerJsonBuilder builder = ScannerJsonBuilder.NewInstance();


            Assembly targetAssembly = Assembly.LoadFile(target);
            Console.WriteLine(targetAssembly.GetName().Name + " " + targetAssembly.GetName().GetPublicKey());

            //Prime the output model
            builder.ProjectName = targetAssembly.GetName().Name;
            builder.Release = targetAssembly.GetName().Version.ToString();
            builder.AddDirectory(targetAssembly.GetName().Name, new FileInfo(target).FullName);

            //The files already scanned
            ISet<String> scannedPaths = new HashSet<String>();
            //The files found that need scanning
            Queue<AssemblyName> assembliesToScan = new Queue<AssemblyName>();

            assembliesToScan.EnqueueAll(targetAssembly.GetReferencedAssemblies());

            while (assembliesToScan.Count > 0)
            {
                AssemblyName refAssemblyName = assembliesToScan.Dequeue();
                Assembly refAssembly = Assembly.Load(refAssemblyName);
                String path = Path.GetFullPath(refAssembly.Location);
                if (scannedPaths.Contains(path))
                    continue;

                try
                {
                    scannedPaths.Add(path);
                    //Note the file informatoin
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(path);
                    string name = string.IsNullOrWhiteSpace(fvi.ProductName) ? refAssembly.GetName().Name : fvi.ProductName;
                    string fileName = Path.GetFileName(path);
                    //We'll make our file names more descriptive than just the actual file name.
                    string fileEntry = ($"{fileName} - {name}[{fvi.ProductVersion}]");
                    Console.WriteLine(fileEntry);

                    bool blacklisted = false;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        string sha1 = computeSha1(path);
                        if (fileName != Blacklist.Instance.Contains(sha1))
                            builder.AddFile(fileEntry, path, new FileInfo(path).Length, sha1);
                        else blacklisted = true;
                    }
                    if (!blacklisted)
                        assembliesToScan.EnqueueAll(refAssembly.GetReferencedAssemblies());


                    var pInvokePaths = PInvokeSearcher.findPInvokePaths(refAssembly);
                    foreach (string nativeDllPath in pInvokePaths.FoundPaths.Where(fp => !scannedPaths.Contains(fp)))
                    {
                        FileVersionInfo dllVersionInfo = FileVersionInfo.GetVersionInfo(nativeDllPath);
                        String dllFileName = ($"{Path.GetFileName(nativeDllPath)} - {dllVersionInfo.ProductName}[{dllVersionInfo.ProductVersion}]");
                        string sha1 = computeSha1(nativeDllPath);
                        if (!string.Equals(Blacklist.Instance.Contains(sha1), dllFileName))
                        {
                            Console.WriteLine("NATIVE: " + dllFileName);
                            builder.AddFile(dllFileName, nativeDllPath, new FileInfo(nativeDllPath).Length, sha1);
                        }
                        scannedPaths.Add(nativeDllPath);
                    }

                }
                catch (FileNotFoundException e)
                {
                    Console.Error.WriteLine(e.Message);
                }
            }

            if (args.Length < 2)
            {
                //No output argument. Do we have a preconfigured output location?
                if (Settings.Instance.Url != null)
                    HubUpload.UploadScan(Settings.Instance.Url, Settings.Instance.Username, Settings.Instance.Password, builder);
                else
                    builder.Write(Console.Out);
            }
            else using (var fileWriter = new StreamWriter(args[1], false))
                {
                    builder.Write(fileWriter);
                }

        }

        private static String computeSha1(String path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                byte[] hash = sha1.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }



    }


}