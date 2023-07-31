using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HRDownpatch
{
    // Games
    public enum Game
    {
        MCC,
        Infinite
    }

    // Information for a single depot download
    public struct Manifest
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public string AppID { get; set; }
        public string DepotID { get; set; }
        public string ManifestID { get; set; }
        public UInt64 TotalSizeBytes { get; set; }
        public string ReleaseDateFull { get; set; }
    }

    // An Install Group is a set of manifests that work together to form a single installation.
    public struct InstallGroup
    {
        public Game GameType { get; set; }
        public string Slug { get; set; }
        public string Name { get; set; }
        public string ReleaseDate { get; set; }
        public string ReleaseDateFull { get; set; }
        public string WaypointLink { get; set; }
        public string ShortDesciprion { get; set; }
        public string LongDescription { get; set; }
        public string Depot1 { get; set; }
        public string Depot2 { get; set; }
        public string Depot3 { get; set; }
        public string Depot4 { get; set; }
        public string Depot5 { get; set; }
        public string Depot6 { get; set; }
        public string Depot7 { get; set; }
        public string Depot8 { get; set; }
    }

    // JSON serialized container
    public class DepotManifest
    {
        public string? CurrentDownpatchVersion { get; set; }
        public string? CurrentDownpatchLink { get; set; }
        public List<Manifest>? Manifests { get; set; }
        public List<InstallGroup>? InstallGroups { get; set; }
    }
}
