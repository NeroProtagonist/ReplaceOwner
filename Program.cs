using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ReplaceOwner
{
    class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("usage: ReplaceOwner --root <searchRootDirectory> --to <newOwner> [--from <currentOwner>] [--dry-run]");
        }

        static uint sNumChanged = 0;
        static bool sDryRun = false;

        static void Main(string[] args)
        {
            string root = "";
            string newOwner = "";
            string currentOwner = "";
            for (uint argIndex = 0; argIndex < args.Length; ++argIndex) {
                string op = args[argIndex];
                switch (op) {
                    case "--root": {
                        ++argIndex;
                        if (argIndex < args.Length) {
                            root = args[argIndex];
                        } else {
                            PrintUsage();
                            return;
                        }
                        break;
                    }
                    case "--to": {
                        ++argIndex;
                        if (argIndex < args.Length) {
                            newOwner = args[argIndex];
                        } else {
                            PrintUsage();
                            return;
                        }
                        break;
                    }
                    case "--from": {
                        ++argIndex;
                        if (argIndex < args.Length) {
                            currentOwner = args[argIndex];
                        } else {
                            PrintUsage();
                            return;
                        }
                        break;
                    }
                    case "--dry-run": {
                        sDryRun = true;
                        break;
                    }
                    default: {
                        Console.WriteLine("Unknown option '{0}'", op);
                        PrintUsage();
                        break;
                    }
                }
            }

            if (root == "" || newOwner == "") {
                PrintUsage();
                return;
            }

            // Parse owner strings
            string SIDPattern = @"^S-\d-\d+-(\d+-){1,14}\d+$";

            System.Security.Principal.IdentityReference currentOwnerId = null;
            if (currentOwner != "") {
                if (Regex.IsMatch(currentOwner, SIDPattern)) {
                    Console.WriteLine("Current owner '{0}' is a SID", currentOwner);
                    currentOwnerId = new System.Security.Principal.SecurityIdentifier(currentOwner);
                } else {
                    Console.WriteLine("Current owner '{0}' is an NT Account", currentOwner);
                    currentOwnerId = new System.Security.Principal.NTAccount(currentOwner);
                }
            }

            System.Security.Principal.IdentityReference newOwnerId;
            if (Regex.IsMatch(newOwner, SIDPattern)) {
                Console.WriteLine("New owner '{0}' is a SID", newOwner);
                newOwnerId = new System.Security.Principal.SecurityIdentifier(newOwner);
            } else {
                Console.WriteLine("New owner '{0}' is an NT Account", newOwner);
                newOwnerId = new System.Security.Principal.NTAccount(newOwner);
            }
            Console.WriteLine();
            Console.WriteLine();

            uint numDirsToProcess = 0;
            uint numDirsProcessed = 0;
            uint numFilesProcessed = 0;
            uint numReparsePoints = 0;
            DirectoryInfo rootDir = null;
            try {
                rootDir = new DirectoryInfo(root);
            } catch (Exception exception) {
                Console.WriteLine("Error opening directory '{0}': {1}", root, exception.Message);
            }
            if (rootDir != null && rootDir.Exists) {
                Stack<DirectoryInfo> searchStack = new Stack<DirectoryInfo>();
                HashSet<string> visitedDirs = new HashSet<string>();
                searchStack.Push(rootDir);
                visitedDirs.Add(rootDir.FullName);
                ++numDirsToProcess;
                ChangeOwner(rootDir, currentOwnerId, newOwnerId);

                while (searchStack.Count != 0) {
                    DirectoryInfo dirInfo = searchStack.Pop();
                    ++numDirsProcessed;

                    Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - 2));

                    try {
                        // Change all files in dir
                        IEnumerable<FileInfo> fileEntries = dirInfo.EnumerateFiles();
                        foreach (FileInfo fileEntry in fileEntries) {
                            ChangeOwner(fileEntry, currentOwnerId, newOwnerId);
                            ++numFilesProcessed;
                        }

                        // Change and add all subdirs
                        IEnumerable<DirectoryInfo> dirEntries = dirInfo.EnumerateDirectories();
                        foreach (DirectoryInfo dirEntry in dirEntries) {
                            // Ignore reparse points
                            if ((dirEntry.Attributes & FileAttributes.ReparsePoint) == 0) {
                                ++numDirsToProcess;
                                ChangeOwner(dirEntry, currentOwnerId, newOwnerId);
                                searchStack.Push(dirEntry);
                                visitedDirs.Add(dirEntry.FullName);
                            } else {
                                ++numReparsePoints;
                            }
                        }
                    } catch (Exception exception) {
                        Console.WriteLine("Exception '{0}'", exception.Message);
                    }

                    Console.WriteLine("Processed {0}\\{1} directories", numDirsProcessed, numDirsToProcess);
                    Console.WriteLine("Processed {0} files", numFilesProcessed);
                }
            }

            Console.WriteLine("-------------------------");
            Console.WriteLine("         Summary");
            Console.WriteLine("-------------------------");
            Console.WriteLine("Successfully changed {0} directories and files", sNumChanged);
            Console.WriteLine("Skipped {0} linked directories", numReparsePoints);
            Console.WriteLine();
        }

        static void CheckAttributes(FileSystemInfo fsInfo)
        {
            FileAttributes fsAttributes = fsInfo.Attributes;
            // Remove flags that are OK to process
            fsAttributes &= ~(FileAttributes.ReadOnly
                            | FileAttributes.Hidden
                            | FileAttributes.System
                            | FileAttributes.Directory
                            | FileAttributes.Archive
                            | FileAttributes.NotContentIndexed
                            | FileAttributes.Temporary
                            | FileAttributes.ReparsePoint);
            if (fsAttributes != 0) {
                throw new Exception(string.Format("FS attributes for '{0}' unknown (TODO: handle)", fsInfo.FullName));
            }
        }

        static void ChangeOwner(FileInfo fileInfo, System.Security.Principal.IdentityReference currentOwnerId, System.Security.Principal.IdentityReference newOwnerId)
        {
            CheckAttributes(fileInfo);
            try {
                FileSecurity fileSec = fileInfo.GetAccessControl();
                if (fileSec.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)) == currentOwnerId) {
                    ++sNumChanged;
                    if (!sDryRun) {
                        fileSec.SetOwner(newOwnerId);
                        fileInfo.SetAccessControl(fileSec);                        
                    }
                    Console.WriteLine("Set ownership on file '{0}' to '{1}'", fileInfo.FullName, newOwnerId.ToString());
                }
            } catch (ArgumentException) {
                Console.WriteLine("Weird exception processing file '{0}'", fileInfo.FullName);
            } catch (Exception exception) {
                Console.WriteLine("Exception processing file '{0}': '{1}'", fileInfo.FullName, exception.Message);
            }
        }

        static void ChangeOwner(DirectoryInfo dirInfo, System.Security.Principal.IdentityReference currentOwnerId, System.Security.Principal.IdentityReference newOwnerId)
        {
            CheckAttributes(dirInfo);            
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) {
                // Ignore reparse points
                
                return;
            }
            try {
                DirectorySecurity dirSec = dirInfo.GetAccessControl();
                if (dirSec.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)) == currentOwnerId) {
                    ++sNumChanged;
                    if (!sDryRun) {
                        dirSec.SetOwner(newOwnerId);
                        dirInfo.SetAccessControl(dirSec);
                    }
                    Console.WriteLine("Set ownership on directory '{0}' to '{1}'", dirInfo.FullName, newOwnerId.ToString());
                }
            } catch (Exception exception) {
                Console.WriteLine("Exception processing file '{0}': '{1}'", dirInfo.FullName, exception.Message);
            }
        }
    }
}
