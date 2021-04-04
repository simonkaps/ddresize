using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace ddresize
{
    class USBDrive
    {
        public string mediatype { get; set; }
        public string interfacetype { get; set; }
        public Int64 size { get; set; }
        public string name { get; set; }
        public string model { get; set; }
        public Int32 index { get; set; }
    }
    
    internal static class Program
    {
        const int FILE_ATTRIBUTE_SYSTEM = 0x4;
        const int FILE_FLAG_SEQUENTIAL_SCAN = 0x8;
        private static readonly string progloc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string fileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess fileAccess, [MarshalAs(UnmanagedType.U4)] FileShare fileShare,
            IntPtr securityAttributes, [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition, int flags, IntPtr template);

        private static void create_imagefile(string srcDrive, string destFile, long bytes)
        {
            using (SafeFileHandle device = CreateFile(srcDrive,
                FileAccess.Read, FileShare.Write | FileShare.Read | FileShare.Delete, IntPtr.Zero, FileMode.Open,
                FILE_ATTRIBUTE_SYSTEM | FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero))
            {
                if (device.IsInvalid)
                {
                    throw new IOException("Unable to access drive. Win32 Error Code " + Marshal.GetLastWin32Error());
                }
                using (FileStream dest = File.Open(destFile, FileMode.Create))
                {
                    using (FileStream src = new FileStream(device, FileAccess.Read))
                    {
                        //src.CopyTo(dest);
                        CopyFileStream(src, dest, bytes);
                    }
                }
            }
        }
        
        private static void CopyFileStream(FileStream input, FileStream output, long bytes)
        {
            byte[] buffer = new byte[81920];
            int read;
            long bytescount = 0;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                bytescount += read;
                drawTextProgressBar(bytescount, bytes);
                if (bytescount >= bytes) break;
            }
        }
        

        private static string sizesuf(long value, string format, bool displayformat)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = value;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                if (format == sizes[order]) break;
                order++;
                len = len/1024;
            }
            return displayformat ? $"{Math.Floor(len):0.#}{sizes[order]}" : $"{Math.Floor(len):0.#}";
        }
        
        private static List<USBDrive> nuDetectUSB()
        {
            ConnectionOptions connOptions = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true
            };
            ManagementScope scope = new ManagementScope("root\\CIMV2", connOptions);
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_DiskDrive");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            var results = new List<USBDrive>();
            foreach (ManagementBaseObject queryObj in searcher.Get())
            {
                var res = new USBDrive
                {
                    mediatype = (string) queryObj["MediaType"],
                    index = Int32.Parse(queryObj["Index"].ToString()),
                    interfacetype = (string) queryObj["InterfaceType"],
                    model = (string) queryObj["Model"],
                    name = (string) queryObj["Name"],
                    size = Int64.Parse(queryObj["Size"].ToString())
                };
                if (res.mediatype == "Removable Media" && res.interfacetype == "USB")
                {
                    results.Add(res);
                }
            }

            return results;
        }

        private static void PrintExamples()
        {
            Console.WriteLine();
            Console.WriteLine("ex: " + progloc + " -l");
            Console.WriteLine("ex: " + progloc + " -s E: -d tempfile.img");
        }
        
        public static void Main(string[] args)
        {
            var optargs = new Arguments("ddresize by Simon Kapsalis (simonkapsal@gmail.com)", progloc);
            var list = optargs.AddSwitch("l", "list", "display list of usb mass storage devices");
            var selecteddrive = optargs.Add<string>("s", "source", "source drive letter of USB mass storage device", false);
            var destfile = optargs.Add<string>("d", "destination", "destination file to write", false);
            optargs.Parse(args);
            
            if (args.Length == 0)
            {
                optargs.PrintUsage(Console.Out);
                PrintExamples();
                Environment.Exit(1);
            }
            if (!optargs.IsValid)
            {
                Console.WriteLine("Invalid arguments");
                optargs.PrintErrors(Console.Error);
                optargs.PrintUsage(Console.Out);
                PrintExamples();
                Environment.Exit(1);
            }

            if (list.WasProvided)
            {
                Console.WriteLine($"{"Index",-10}{"Model",-50}{"Name",-30}{"Size",-10}");
                foreach (var drv in nuDetectUSB())
                {
                    Console.WriteLine($"{drv.index,-10}{drv.model,-50}{drv.name,-30}{sizesuf(drv.size, "MB", true),-10}");
                }
                Environment.Exit(0);
            }

            if (!selecteddrive.WasProvided && destfile.WasProvided)
            {
                Console.WriteLine("Invalid arguments.\nWe need both source and destination!");
                Environment.Exit(1);
            }
            
            if (selecteddrive.WasProvided && !destfile.WasProvided)
            {
                Console.WriteLine("Invalid arguments.\nWe need both source and destination!");
                Environment.Exit(1);
            }
            
            if (selecteddrive.WasProvided && destfile.WasProvided)
            {
                var dstFile = verifyFile(destfile.Value);
                if (String.IsNullOrEmpty(dstFile))
                {
                    Console.WriteLine("Destination file seems to exist. Aborting...");
                    Environment.Exit(1);
                }
                var srcDriveLetter = selecteddrive.Value;
                var srcPhysicalDrive = GetPhysicalDevicePath(srcDriveLetter);
                var regex = new Regex(@"\d+");
                var srcDriveIndex = regex.Match(srcPhysicalDrive).Value;
                Console.WriteLine("Drive selected was: " + srcDriveLetter);
                Console.WriteLine("\nhas following partitions:");
                
                var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_DiskPartition WHERE DiskIndex="+srcDriveIndex);
                long totalbytesToWrite = 0;
                foreach (var queryObj in searcher.Get())
                {
                    totalbytesToWrite += long.Parse(queryObj["Size"].ToString());
                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine("Name:{0}", (string)queryObj["Name"]);
                    Console.WriteLine("Size:{0} bytes", queryObj["Size"].ToString());
                }
                totalbytesToWrite += 1048576;
                Console.WriteLine("\nTotal to write to destination file(added 1MB unallocated for safety): " + sizesuf(totalbytesToWrite, "MB", true));
                Console.WriteLine("Destination file: {0}", dstFile);
                Console.Write("\nIs the above information correct? Do you want to continue? [y/n]");
                ConsoleKey response = Console.ReadKey(false).Key;
                if (response.ToString().ToLower() == "y")
                {
                    Console.WriteLine();
                    create_imagefile(srcPhysicalDrive, dstFile, totalbytesToWrite);
                    Console.WriteLine("\nDone!");
                    Environment.Exit(0);
                }
                else
                {
                    Console.WriteLine("\nAborted!");
                    Environment.Exit(0);
                }
            }
        }

        private static string GetPhysicalDevicePath(string DriveLetter)
        {
            char DrvLetter = DriveLetter.Substring(0, 1).ToCharArray()[0];
            ManagementClass devs = new ManagementClass( @"Win32_Diskdrive");
            {
                ManagementObjectCollection moc = devs.GetInstances();
                foreach(ManagementObject mo in moc)
                {
                    foreach (ManagementObject b in mo.GetRelated("Win32_DiskPartition"))
                    {
                        foreach (ManagementBaseObject c in b.GetRelated("Win32_LogicalDisk"))
                        {
                            string DevName = string.Format("{0}", c["Name"]);
                            if (DevName[0] == DrvLetter) return string.Format("{0}", mo["DeviceId"]); 
                        }
                    }
                }
            }
            return "";
        }
        
        private static void drawTextProgressBar(long progress, long total)
        {
            //draw empty progress bar
            Console.CursorLeft = 0;
            Console.Write("["); //start
            Console.CursorLeft = 32;
            Console.Write("]"); //end
            Console.CursorLeft = 1;
            float onechunk = 30.0f / total;

            //draw filled part
            int position = 1;
            for (int i = 0; i < onechunk * progress; i++)
            {
                Console.BackgroundColor = ConsoleColor.Green;
                Console.CursorLeft = position++;
                Console.Write(" ");
            }

            //draw unfilled part
            for (int i = position; i <= 31 ; i++)
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.CursorLeft = position++;
                Console.Write(" ");
            }

            //draw totals
            Console.CursorLeft = 35;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(sizesuf(progress, "MB", true) + " of " + sizesuf(total, "MB", true) + "    ");
        }

        private static string verifyFile(string file)
        {
            string newfile = Path.Combine(Path.GetDirectoryName(progloc), file);
            if (!File.Exists(newfile)) return newfile;
            return String.Empty;
        }
        
    }
}