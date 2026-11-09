using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// Gara!'s three tracks (2026-11-08), each about 830-940 m (a lap ~35 s), laid out so the tightest corner is no
    /// tighter than an 18 m radius (KartTrackTests; the layouts were drawn and checked first). The jumps are on the long
    /// straights, the gift boxes where the road is straight enough for everyone to see them coming, boost pads on alternate
    /// sides, lines of stars on the racing line and off it.
    /// <list type="bullet">
    /// <item><b>Bregdeti</b> (the coast): a long straight along the sea, a hairpin at its end, S-bends up through the
    /// olive groves, a sweeper back down to the beach.</item>
    /// <item><b>Fshati</b> (the village): round the hill with the castle, up and down through the stone houses.</item>
    /// <item><b>Lugina e Dinozaurëve</b> (Vrapo!'s dino valley): flowing curves round the volcano, a double jump on the
    /// long straight.</item>
    /// </list>
    /// </summary>
    public static class KartTracks
    {
        public const int Count = 3;

        static readonly KartTrack[] Cache = new KartTrack[Count];

        public static KartTrack Get(int index)
        {
            int i = ((index % Count) + Count) % Count;
            return Cache[i] ??= Build(i);
        }

        public static KartTrack Get(KartTheme theme) => Get((int)theme);

        static Vector2 V(float x, float z) => new Vector2(x, z);

        static KartTrack Build(int index)
        {
            switch (index)
            {
                case 0:
                    return new KartTrack("coast", KartTheme.Coast,
                        new[]
                        {
                            V(0, 0), V(90, -4), V(160, 0), V(205, 22), V(214, 62), V(188, 92), V(150, 96), V(122, 120), V(118, 160),
                            V(88, 186), V(40, 178), V(10, 150), V(-20, 120), V(-62, 118), V(-96, 96), V(-104, 56), V(-80, 18),
                        },
                        new[] { V(0f, 0f), V(0.22f, 0.5f), V(0.42f, 5.5f), V(0.55f, 7f), V(0.68f, 3.5f), V(0.86f, 0.5f) },
                        new List<TrackFeature>
                        {
                            TrackFeature.Boxes(42f),
                            TrackFeature.Ramp(112f),
                            TrackFeature.StarLine(168f, 3f),
                            TrackFeature.Pad(262f, -3f),
                            TrackFeature.StarLine(330f, -3f),
                            TrackFeature.Pad(420f, 3f),
                            TrackFeature.StarLine(452f, 0f),
                            TrackFeature.Boxes(545f),
                            TrackFeature.StarLine(598f, 2.5f),
                            TrackFeature.Pad(704f, -3f),
                            TrackFeature.StarLine(722f, -3f),
                            TrackFeature.Ramp(792f),
                        });
                case 1:
                    return new KartTrack("village", KartTheme.Village,
                        new[]
                        {
                            V(0, 0), V(70, -10), V(130, 4), V(172, 44), V(170, 96), V(138, 124), V(112, 160), V(104, 204), V(72, 236),
                            V(22, 240), V(-22, 214), V(-48, 170), V(-86, 128), V(-102, 80), V(-74, 30),
                        },
                        new[] { V(0f, 0f), V(0.18f, 2.5f), V(0.34f, 8f), V(0.5f, 12f), V(0.63f, 9f), V(0.77f, 4f), V(0.9f, 1f) },
                        new List<TrackFeature>
                        {
                            TrackFeature.Boxes(40f),
                            TrackFeature.Ramp(96f),
                            TrackFeature.StarLine(140f, 3f),
                            TrackFeature.Pad(200f, -3f),
                            TrackFeature.StarLine(250f, -2.5f),
                            TrackFeature.Pad(340f, 3f),
                            TrackFeature.StarLine(382f, 3f),
                            TrackFeature.Boxes(452f),
                            TrackFeature.Pad(520f, -3f),
                            TrackFeature.Ramp(612f),
                            TrackFeature.StarLine(660f, 0f),
                            TrackFeature.StarLine(722f, -3f),
                            TrackFeature.Pad(790f, 3f),
                        });
                default:
                    return new KartTrack("dino", KartTheme.Dino,
                        new[]
                        {
                            V(0, 0), V(80, 0), V(150, 10), V(200, 46), V(210, 100), V(176, 140), V(130, 148), V(100, 182), V(60, 214),
                            V(10, 214), V(-36, 186), V(-50, 152), V(-44, 110), V(-70, 76), V(-104, 66), V(-130, 38), V(-120, 2),
                            V(-84, -12), V(-40, -10),
                        },
                        new[] { V(0f, 0f), V(0.24f, 2f), V(0.4f, 6.5f), V(0.55f, 3f), V(0.7f, 7f), V(0.86f, 2f) },
                        new List<TrackFeature>
                        {
                            TrackFeature.Boxes(40f),
                            TrackFeature.Ramp(100f),
                            TrackFeature.Ramp(168f),
                            TrackFeature.StarLine(210f, 3f),
                            TrackFeature.Pad(262f, -3f),
                            TrackFeature.StarLine(322f, -3f),
                            TrackFeature.Pad(402f, 3f),
                            TrackFeature.StarLine(470f, 0f),
                            TrackFeature.Boxes(560f),
                            TrackFeature.StarLine(612f, 3f),
                            TrackFeature.Pad(662f, 0f),
                            TrackFeature.StarLine(762f, -3f),
                            TrackFeature.Pad(862f, 3f),
                            TrackFeature.StarLine(890f, 0f),
                        });
            }
        }
    }
}
