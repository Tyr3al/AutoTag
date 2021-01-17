namespace autotag.Core {
    public class AutoTagConfig {
        public enum Modes { TV, Movie };
        public static int currentVer = 2;
        public int configVer { get; set; } = currentVer;
        public Modes mode { get; set; } = Modes.TV;
        public bool manualMode { get; set; } = false;
        public bool verbose { get; set; } = false;
        public bool addCoverArt { get; set; } = true;
        public bool tagFiles { get; set; } = true;
        public bool renameFiles { get; set; } = true;
        public string tvRenamePattern { get; set; } = "%1 - %2x%3 - %4";
        public string movieRenamePattern { get; set; } = "%1 (%2)";
        public string parsePattern { get; set; } = "";
    }
}