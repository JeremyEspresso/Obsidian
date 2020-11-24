﻿using SharpNoise;
using SharpNoise.Modules;
using System;

namespace Obsidian.WorldData.Generators.Overworld
{
    public class OverworldNoise
    {
        private Module cavePerlin;
        private Module oreNoise;

        private Module BiomeNoise;
        private Module BiomeHumidity;

        private OverworldTerrainSettings generatorSettings;
        private OverworldTerrainGenerator generator;
        private Module generatorModule;

        public OverworldNoise(int seed)
        {
            generatorSettings = new OverworldTerrainSettings()
            {
                Seed = seed
            };

            generator = new OverworldTerrainGenerator(generatorSettings);
            generatorModule = generator.CreateModule();

            cavePerlin = new Simplex
            {
                Frequency = 13.37,
                OctaveCount = 1,
            };

            oreNoise = new Multiply
            {
                Source0 = new Billow()
                {
                    Seed = generatorSettings.Seed + 101,
                    Frequency = 13.37,
                    Lacunarity = generatorSettings.ContinentLacunarity,
                    OctaveCount = 2,
                    Quality = NoiseQuality.Fast,
                },
                Source1 = new Perlin
                {
                    Frequency = 1.14,
                    Lacunarity = 2.222,
                    OctaveCount = 2,
                    Seed = generatorSettings.Seed+102
                }
            };

            BiomeNoise = new Turbulence()
            {
                Frequency = 43.25,
                Power = 0.01,
                Roughness = 6,
                Seed = generatorSettings.Seed + 100,
                Source0 = new Add()
                {
                    Source0 = new Clamp()
                    {
                        UpperBound = 2,
                        LowerBound = -0.1,
                        Source0 = new Billow()
                        {
                            Seed = generatorSettings.Seed + 101,
                            Frequency = 43.25,
                            Lacunarity = generatorSettings.ContinentLacunarity,
                            OctaveCount = 1,
                            Quality = NoiseQuality.Fast,
                        }
                    },
                    Source1 = new Clamp()
                    {
                        UpperBound = 0.1,
                        LowerBound = -2,
                        Source0 = new Invert()
                        {
                            Source0 = new Billow()
                            {
                                Seed = generatorSettings.Seed + 102,
                                Frequency = 43.25,
                                Lacunarity = generatorSettings.ContinentLacunarity,
                                OctaveCount = 1,
                                Quality = NoiseQuality.Fast,
                            }
                        }
                    }
                }
            };

            BiomeHumidity = new Turbulence()
            {
                Frequency = 47.25,
                Power = 0.01,
                Roughness = 6,
                Seed = generatorSettings.Seed + 103,
                Source0 = new Add()
                {
                    Source0 = new Clamp()
                    {
                        UpperBound = 2,
                        LowerBound = -0.1,
                        Source0 = new Billow()
                        {
                            Seed = generatorSettings.Seed + 104,
                            Frequency = 47.25,
                            Lacunarity = generatorSettings.ContinentLacunarity,
                            OctaveCount = 1,
                            Quality = NoiseQuality.Fast,
                        }
                    },
                    Source1 = new Clamp()
                    {
                        UpperBound = 0.1,
                        LowerBound = -2,
                        Source0 = new Invert()
                        {
                            Source0 = new Billow()
                            {
                                Seed = generatorSettings.Seed + 105,
                                Frequency = 47.25,
                                Lacunarity = generatorSettings.ContinentLacunarity,
                                OctaveCount = 1,
                                Quality = NoiseQuality.Fast,
                            }
                        }
                    }
                }
            };

        }

        public double Terrain(float x, float z)
        {

            //return generatorModule.GetValue(x, 5, z);
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) * generatorSettings.TerrainVertStretch;
            //return generator.CreateScaledPlainsTerrain(generator.CreatePlainsTerrain()).GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) * generatorSettings.TerrainVertStretch + 27;
        }

        public double Underground(float x, float z)
        {
            return generatorModule.GetValue(x * generatorSettings.TerrainHorizStretch, 1, z * generatorSettings.TerrainHorizStretch) / 45.0;
        }

        public double Bedrock(float x, float z)
        {
            return generatorModule.GetValue(x, 2, z) / 30.0;
        }

        public bool Cave(float x, float y, float z)
        {
            var value = cavePerlin.GetValue(x * generatorSettings.CaveHorizStretch, y * generatorSettings.CaveVertStretch, z * generatorSettings.CaveHorizStretch);
            return !(value < generatorSettings.CaveFillPercent + 0.1 && value > -0.1);
        }

        public bool Coal(float x, float y, float z)
        {
            var value = oreNoise.GetValue(x / 18, y / 6, z / 18);
            return value < 0.55 && value > 0.5;
        }

        public bool Iron(float x, float y, float z)
        {
            var value = oreNoise.GetValue(x / 25, (y / 20) + 100, z / 25);
            return value < 0.65 && value > 0.6;
        }

        public bool Redstone(float x, float y, float z)
        {
            var value = oreNoise.GetValue(x / 18, (y / 18) + 300, z / 18);
            return value < 0.95 && value > 0.8;
        }

        public bool Diamond(float x, float y, float z)
        {
            var value = oreNoise.GetValue(x, (y) + 900, z);
            return value > 0.2;
        }










        public bool isRiver(float x, float z)
        {
            var value = generator.RiversPos.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value < 0.5 && !isOcean(x, z) && !isDeepOcean(x, z);
        }

        public bool isMountain(float x, float z)
        {
            return generator.ContinentsWithMountains.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) > 0.5;
        }

        public bool isHills(float x, float z)
        {
            var value = generator.ContinentsWithHills.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value > -0.05;
        }

        public bool isBadlands(float x, float z)
        {
            var value = generator.ContinentsWithBadlands.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value > 0.5;
        }

        public bool isPlains(float x, float z)
        {
            var value = generator.ContinentsWithPlains.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value > -0.15;
        }

        public int OceanFloor(int x, int z)
        {
            return (int)Math.Min(generator.BaseContinents.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch) * generatorSettings.TerrainVertStretch, 0);
        }

        public bool isOcean(float x, float z)
        {
            var value = generator.BaseContinents.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value < 0 && !isPlains(x, z);
        }

        public bool isDeepOcean(float x, float z)
        {
            var value = generator.BaseContinents.GetValue(x * generatorSettings.TerrainHorizStretch, 0, z * generatorSettings.TerrainHorizStretch);
            return value < -0.4 && !isPlains(x, z);
        }

        public double GetBiomeTemp(int x, int y, int z)
        {
            return BiomeNoise.GetValue(x * generatorSettings.TerrainHorizStretch / 6, 0, z * generatorSettings.TerrainHorizStretch / 6);
        }

        public double GetBiomeHumidity(int x, int y, int z)
        {
            return BiomeHumidity.GetValue(x * generatorSettings.TerrainHorizStretch / 6, 0, z * generatorSettings.TerrainHorizStretch / 6);
        }

        /// <summary>
        /// Returns values b/w 0 and 5;
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public double Decoration(double x, double y, double z) => oreNoise.GetValue(x, y + 5, z);
    }
}
