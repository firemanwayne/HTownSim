using System;
using UnityEditor;
using UnityEngine;

/// <summary>Generates tileable detail and normal textures and moves the ground and building materials onto the world-space triplanar shader.</summary>
public static class ApplyTextures
{
    private const int Size = 512;
    private const string TextureDir = "Assets/HTownSim/Textures/";
    private const string MaterialDir = "Assets/HTownSim/Generated/";

    private sealed class Recipe
    {
        public string Name = "";
        public Func<float, float, float> Height = (u, v) => 0f;
        public float Contrast = 1f;
        public float NormalStrength = 3f;
    }

    /// <summary>Bakes the textures and applies them to the materials.</summary>
    public static string Run()
    {
        var recipes = new[]
        {
            new Recipe { Name = "AsphaltDetail", Contrast = 0.9f, NormalStrength = 4f, Height = (u, v) => 0.55f * Fbm(u, v, 64, 3, 11) + 0.45f * Fbm(u, v, 160, 2, 12) },
            new Recipe { Name = "ConcreteDetail", Contrast = 0.7f, NormalStrength = 2f, Height = (u, v) => 0.5f * Fbm(u, v, 6, 4, 21) + 0.35f * Fbm(u, v, 90, 2, 22) + 0.15f * Slabs(u, v, 4) },
            new Recipe { Name = "BrickDetail", Contrast = 1.1f, NormalStrength = 5f, Height = (u, v) => Bricks(u, v) },
            new Recipe { Name = "StuccoDetail", Contrast = 0.6f, NormalStrength = 3.5f, Height = (u, v) => 0.6f * Fbm(u, v, 40, 3, 31) + 0.4f * Fbm(u, v, 12, 3, 32) },
            new Recipe { Name = "GrassDetail", Contrast = 1.0f, NormalStrength = 3f, Height = (u, v) => 0.5f * Fbm(u, v, 128, 2, 41) + 0.5f * Fbm(u, v, 10, 3, 42) },
        };

        foreach (var recipe in recipes)
        {
            Bake(recipe);
        }

        AssetDatabase.Refresh();

        var shader = Shader.Find("HTownSim/WorldTriplanar");

        if (shader == null)
        {
            throw new InvalidOperationException("Shader HTownSim/WorldTriplanar not found (compile error?).");
        }

        Apply("Asphalt", "AsphaltDetail", shader, 6f, 1f, 1f);
        Apply("LotAsphalt", "AsphaltDetail", shader, 6f, 1f, 1f);
        Apply("Concrete", "ConcreteDetail", shader, 5f, 0.8f, 1f);
        Apply("Granite", "ConcreteDetail", shader, 3f, 0.5f, 0.8f);
        Apply("Limestone", "ConcreteDetail", shader, 4f, 0.6f, 0.9f);
        Apply("Brick", "BrickDetail", shader, 3f, 1f, 1f);
        Apply("Stucco", "StuccoDetail", shader, 3f, 0.8f, 1f);
        Apply("SageStucco", "StuccoDetail", shader, 3f, 0.8f, 1f);
        Apply("ParkGrass", "GrassDetail", shader, 4f, 0.8f, 1f);
        Apply("LotGrass", "GrassDetail", shader, 4f, 0.8f, 1f);

        AssetDatabase.SaveAssets();

        return "Textures applied.";
    }

    private static void Apply(string materialName, string textureName, Shader shader, float tile, float normal, float detail)
    {
        var path = MaterialDir + materialName + ".mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            Debug.LogWarning("Missing material " + path);

            return;
        }

        var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;

        var smooth = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.2f;

        material.shader = shader;

        material.SetColor("_BaseColor", color);

        material.SetFloat("_Smoothness", smooth);

        material.SetFloat("_Metallic", 0f);

        material.SetFloat("_TileSize", tile);

        material.SetFloat("_BumpScale", normal);

        material.SetFloat("_DetailStrength", detail);

        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + textureName + ".png"));

        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + textureName + "_N.png"));

        EditorUtility.SetDirty(material);
    }

    private static void Bake(Recipe recipe)
    {
        var height = new float[Size * Size];

        var min = float.MaxValue;

        var max = float.MinValue;

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var h = recipe.Height(x / (float)Size, y / (float)Size);

                height[y * Size + x] = h;

                min = Mathf.Min(min, h);

                max = Mathf.Max(max, h);
            }
        }

        var range = max - min + 0.0001f;

        var albedo = new Color32[Size * Size];

        var normal = new Color32[Size * Size];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = y * Size + x;

                var h = (height[i] - min) / range;

                var g = Mathf.Clamp01(0.5f + (h - 0.5f) * 0.5f * recipe.Contrast);

                albedo[i] = new Color32((byte)(g * 255f), (byte)(g * 255f), (byte)(g * 255f), 255);

                var l = height[y * Size + (x + Size - 1) % Size];

                var r = height[y * Size + (x + 1) % Size];

                var d = height[((y + Size - 1) % Size) * Size + x];

                var u = height[((y + 1) % Size) * Size + x];

                var n = new Vector3((l - r) * recipe.NormalStrength * 8f / range, (d - u) * recipe.NormalStrength * 8f / range, 1f).normalized;

                normal[i] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
        }

        Save(recipe.Name + ".png", albedo, false);

        Save(recipe.Name + "_N.png", normal, true);
    }

    private static void Save(string file, Color32[] pixels, bool isNormal)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);

        texture.SetPixels32(pixels);

        System.IO.File.WriteAllBytes(Application.dataPath + "/HTownSim/Textures/" + file, texture.EncodeToPNG());

        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(TextureDir + file);

        var importer = (TextureImporter)AssetImporter.GetAtPath(TextureDir + file);

        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;

        importer.sRGBTexture = !isNormal;

        importer.mipmapEnabled = true;

        importer.anisoLevel = 8;

        importer.wrapMode = TextureWrapMode.Repeat;

        importer.SaveAndReimport();
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);

            h = (h ^ (h >> 13)) * 1274126177u;

            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    private static float Corner(int ix, int iy, int cells, int seed)
    {
        return Hash(((ix % cells) + cells) % cells, ((iy % cells) + cells) % cells, seed);
    }

    private static float Value(float u, float v, int cells, int seed)
    {
        var x = u * cells;

        var y = v * cells;

        var x0 = Mathf.FloorToInt(x);

        var y0 = Mathf.FloorToInt(y);

        var fx = x - x0;

        var fy = y - y0;

        fx = fx * fx * (3f - 2f * fx);

        fy = fy * fy * (3f - 2f * fy);

        var bottom = Mathf.Lerp(Corner(x0, y0, cells, seed), Corner(x0 + 1, y0, cells, seed), fx);

        var top = Mathf.Lerp(Corner(x0, y0 + 1, cells, seed), Corner(x0 + 1, y0 + 1, cells, seed), fx);

        return Mathf.Lerp(bottom, top, fy);
    }

    private static float Fbm(float u, float v, int baseCells, int octaves, int seed)
    {
        var sum = 0f;

        var amp = 0.5f;

        var norm = 0f;

        var cells = baseCells;

        for (var o = 0; o < octaves; o++)
        {
            sum += Value(u, v, cells, seed + o) * amp;

            norm += amp;

            amp *= 0.5f;

            cells *= 2;
        }

        return sum / norm;
    }

    private static float Slabs(float u, float v, int count)
    {
        var fu = Mathf.Abs(Mathf.Repeat(u * count, 1f) - 0.5f) * 2f;

        var fv = Mathf.Abs(Mathf.Repeat(v * count, 1f) - 0.5f) * 2f;

        return -Mathf.Clamp01((Mathf.Max(fu, fv) - 0.97f) * 30f);
    }

    private static float Bricks(float u, float v)
    {
        const int rows = 16;

        const int cols = 8;

        var row = Mathf.FloorToInt(v * rows);

        var local = new Vector2(Mathf.Repeat(u * cols + (row % 2) * 0.5f, 1f), Mathf.Repeat(v * rows, 1f));

        var edge = Mathf.Min(Mathf.Min(local.x, 1f - local.x) * 2.2f, Mathf.Min(local.y, 1f - local.y) * 4f);

        var mortar = Mathf.Clamp01(edge * 3f);

        var brickIndex = Mathf.FloorToInt(u * cols + (row % 2) * 0.5f);

        var tone = Hash(((brickIndex % cols) + cols) % cols, row, 77) * 0.35f;

        return mortar * (0.65f + tone + Fbm(u, v, 96, 2, 78) * 0.25f);
    }
}
