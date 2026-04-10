using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SqlPilot.Installer.ViewModels;

namespace SqlPilot.Installer.Services
{
    /// <summary>
    /// Static catalog of every supported SSMS version + runtime detection of which
    /// ones are installed on this machine. Mirrors Get-SqlPilotSsmsCatalog in
    /// installer/_SsmsHelpers.ps1 — if you change either, mirror the change in the
    /// other. PowerShell and C# can't share a single data file without adding a
    /// JSON parser to either side, so we accept the deliberate duplication.
    /// </summary>
    internal static class SsmsDetector
    {
        private static readonly SsmsCatalogEntry[] Catalog = new[]
        {
            new SsmsCatalogEntry(
                version: 22,
                label: "SSMS 22",
                idePath: @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE",
                subfolder: "SSMS22",
                dataBase: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS"),
                dataPattern: new Regex(@"^22\.")
            ),
            new SsmsCatalogEntry(
                version: 20,
                label: "SSMS 20",
                idePath: @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE",
                subfolder: "SSMS18-20",
                dataBase: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SQL Server Management Studio"),
                dataPattern: new Regex(@"^20\.")
            ),
            new SsmsCatalogEntry(
                version: 18,
                label: "SSMS 18",
                idePath: @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE",
                subfolder: "SSMS18-20",
                dataBase: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SQL Server Management Studio"),
                dataPattern: new Regex(@"^18\.")
            )
        };

        /// <summary>
        /// Returns one row per installed SSMS version. Each row carries the install
        /// state (whether SQL Pilot is already deployed there and at what version).
        /// </summary>
        public static List<SsmsVersionRow> DetectAll()
        {
            return Catalog
                .Where(entry => Directory.Exists(entry.IdePath))
                .Select(entry => new SsmsVersionRow(entry)
                {
                    InstalledVersion = ReadInstalledVersion(entry.IdePath)
                })
                .ToList();
        }

        /// <summary>
        /// Reads the deployed SqlPilot.Package.pkgdef and parses the
        /// "ProductDetails"="X.Y.Z" line. Returns null if SQL Pilot isn't installed
        /// in this SSMS or the pkgdef doesn't have the line.
        /// </summary>
        private static string ReadInstalledVersion(string idePath)
        {
            try
            {
                var pkgdef = Path.Combine(idePath, "Extensions", "SqlPilot", "SqlPilot.Package.pkgdef");
                if (!File.Exists(pkgdef)) return null;

                foreach (var line in File.ReadLines(pkgdef))
                {
                    var m = Regex.Match(line, "\"ProductDetails\"=\"([^\"]+)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
                return "unknown";
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Static metadata for one SSMS version. Read-only — never modified at runtime.
    /// </summary>
    internal sealed class SsmsCatalogEntry
    {
        public int Version { get; }
        public string Label { get; }
        public string IdePath { get; }
        public string Subfolder { get; }
        public string DataBase { get; }
        public Regex DataPattern { get; }

        public SsmsCatalogEntry(int version, string label, string idePath, string subfolder, string dataBase, Regex dataPattern)
        {
            Version = version;
            Label = label;
            IdePath = idePath;
            Subfolder = subfolder;
            DataBase = dataBase;
            DataPattern = dataPattern;
        }
    }
}
