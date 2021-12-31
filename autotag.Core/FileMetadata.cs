﻿using System;

namespace autotag.Core {
    public class FileMetadata {
        public enum Types { TV, Movie };

        // Common fields
        public Types FileType;
        public int? Id;
        public string Title;
        public string Overview;
        public string CoverURL;
        public string CoverFilename;
        public bool Success;
        public bool Complete;
        public string Director;
        public string[] Actors;
        public string[] Characters;
        public string[] Genres;

        // TV specific fields
        public string SeriesName;
        public int Season;
        public int Episode;
        public int SeasonEpisodes;

        // Movie specific fields
        public DateTime Date;

        public FileMetadata(Types type) {
            FileType = type;
            Success = true;
            Complete = true;
        }

        public override string ToString() {
            if (FileType == Types.TV) {
                if (!string.IsNullOrEmpty(Title)) {
                    return $"{SeriesName} S{Season.ToString("00")}E{Episode.ToString("00")} ({Title})";
                } else {
                    return $"{SeriesName} S{Season.ToString("00")}E{Episode.ToString("00")}";
                }
            } else {
                return $"{Title} ({Date.Year})";
            }
        }
    }
}
