using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Loads textures as raw RGBA32, bypassing RimWorld's content pipeline
    /// which applies destructive DXT block-compression that causes visible
    /// pixel artifacts and color glitches on large UI textures.
    /// </summary>
    public static class TextureLoader
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Load a texture from the mod's Textures folder without DXT compression.
        /// <paramref name="rimTexPath"/> is the RimWorld-style path (e.g. "UI/Forge/Forge_Window_BG").
        /// </summary>
        public static Texture2D LoadUncompressed(string rimTexPath)
        {
            if (string.IsNullOrEmpty(rimTexPath)) return null;
            if (cache.TryGetValue(rimTexPath, out var cached) && cached != null) return cached;

            string[] extensions = { ".png", ".jpg", ".jpeg" };
            foreach (var mod in LoadedModManager.RunningMods)
            {
                foreach (var ext in extensions)
                {
                    string filePath = Path.Combine(
                        mod.RootDir,
                        "Textures",
                        rimTexPath.Replace('/', Path.DirectorySeparatorChar) + ext);

                    if (!File.Exists(filePath)) continue;

                    byte[] data = File.ReadAllBytes(filePath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
                    ImageConversion.LoadImage(tex, data);
                    tex.filterMode = FilterMode.Bilinear;
                    tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                    cache[rimTexPath] = tex;
                    return tex;
                }
            }

            // Fallback to ContentFinder if file not found on disk
            var fallback = ContentFinder<Texture2D>.Get(rimTexPath, false);
            if (fallback != null)
            {
                fallback.filterMode = FilterMode.Bilinear;
                cache[rimTexPath] = fallback;
            }
            return fallback;
        }
    }
}
