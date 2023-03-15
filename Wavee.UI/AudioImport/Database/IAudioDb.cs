﻿using System.Linq.Expressions;
using LiteDB;

namespace Wavee.UI.AudioImport.Database
{
    public interface IAudioDb
    {
        ILiteCollection<LocalAudioFile> AudioFiles
        {
            get;
        }
        IReadOnlyCollection<LocalAudioFile>
            GetLatestAudioFiles<TK>(Expression<Func<LocalAudioFile, TK>> order,
                bool ascending, int offset = 0, int limit = 20);

        bool[] CheckIfAudioFilesExist(IEnumerable<string> paths);
        Task<LocalAudioFile> ImportAudioFile(ImportAudioRequest request);
        LocalAudioFile? GetAudioFile(string path);
        int Count();
        // string GetLinkedPathName(string path);

        IReadOnlyCollection<LocalAudioFile> GetTracksForAlbum(string albumName);
        IReadOnlyCollection<(string Path, DateTime LastUpdatedAt)> GetAll();
        bool Remove(string path);
        void CleanupDuplicates();
    }
}
