namespace InFalsus.Gameplay.Falsum.Core
{
    public sealed class FalsumNewChartRequest
    {
        public string TargetPath;
        public string AudioPath;
        public string CoverPath;
        /// <summary>
        /// The package primary key written into <c>song.json</c> and the
        /// <c># song:</c> header of every chart. Required when creating a
        /// package; must match <see cref="FalsumManifest.IsValidNewSongId"/>.
        /// It no longer has to match the target file name.
        /// </summary>
        public string SongId;
        public string Title;
        public string Artist;
        public string JacketDesigner;
        public readonly bool[] DifficultyEnabled = new bool[4];
        public readonly int[] Ratings = new int[4];
        public readonly string[] ChartDesigners = new string[4];

        public FalsumNewChartRequest()
        {
            for (var i = 0; i < ChartDesigners.Length; i++)
            {
                ChartDesigners[i] = string.Empty;
            }
        }
    }
}
